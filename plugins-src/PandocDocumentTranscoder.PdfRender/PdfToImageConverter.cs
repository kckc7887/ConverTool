using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using DtronixPdf;
using PluginAbstractions;
using SkiaSharp;

namespace PandocDocumentTranscoder.PdfRender;

/// <summary>PDF→PNG/JPG/ZIP using PDFium + SkiaSharp. Shipped as a separate bundle under %LOCALAPPDATA%\ConverTool\tools\document-pdf-render\.</summary>
public static class PdfToImageConverter
{
    public static async Task ConvertAsync(
        ExecuteContext context,
        string pdfTargetId,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var dpi = ParseDpiFromConfig(context.SelectedConfig, 150);
        var innerExt = pdfTargetId == "zip"
            ? ParseZipInnerFormatFromConfig(context.SelectedConfig)
            : pdfTargetId;

        reporter.OnLog(
            pdfTargetId == "zip"
                ? $"[pdf2img] DPI={dpi}, target=ZIP, inner={innerExt.ToUpperInvariant()}"
                : $"[pdf2img] DPI={dpi}, target=long {innerExt.ToUpperInvariant()}");

        var images = new List<SKBitmap>();
        var inputPath = context.InputPath;

        try
        {
            using var pdfDoc = PdfDocument.Load(inputPath, null);
            var pageCount = pdfDoc.Pages;
            reporter.OnLog($"[pdf2img] PDF has {pageCount} pages");

            if (pageCount == 0)
            {
                reporter.OnFailed(new FailedInfo("PDF has no pages.", "NO_PAGES"));
                return;
            }

            for (var i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var page = pdfDoc.GetPage(i);
                var scale = dpi / 72.0f;
                using var pdfBitmap = page.Render(scale, cancellationToken);

                var width = pdfBitmap.Width;
                var height = pdfBitmap.Height;

                var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                var pixels = pdfBitmap.Pointer;
                if (pixels != IntPtr.Zero)
                {
                    var stride = pdfBitmap.Stride;
                    var rowBytes = width * 4;
                    var bufferSize = rowBytes * height;
                    var buffer = new byte[bufferSize];

                    for (var row = 0; row < height; row++)
                    {
                        Marshal.Copy(pixels + row * stride, buffer, row * rowBytes, rowBytes);
                    }

                    var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    try
                    {
                        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        skBitmap.InstallPixels(info, handle.AddrOfPinnedObject(), rowBytes, (addr, ctx) => handle.Free());
                    }
                    catch
                    {
                        handle.Free();
                        throw;
                    }
                }

                images.Add(skBitmap);
                reporter.OnProgress(new ProgressInfo(ProgressStage.Running, (i + 1) * 100 / pageCount));
                reporter.OnLog($"[pdf2img] Rendered page {i + 1}/{pageCount}");
            }

            var imageFormat = innerExt == "jpg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
            var quality = innerExt == "jpg" ? 90 : 100;

            if (pdfTargetId == "zip")
            {
                await OutputAsZipAsync(context, images, imageFormat, quality, innerExt, reporter, cancellationToken);
            }
            else
            {
                await OutputAsLongImageAsync(context, images, imageFormat, quality, innerExt, reporter, cancellationToken);
            }
        }
        finally
        {
            foreach (var img in images)
            {
                img.Dispose();
            }
        }
    }

    private static int ParseDpiFromConfig(IReadOnlyDictionary<string, object?> config, int defaultDpi)
    {
        if (!config.TryGetValue("Dpi", out var v) || v is null)
            return defaultDpi;

        int dpi = defaultDpi;
        switch (v)
        {
            case int i:
                dpi = i;
                break;
            case long l:
                dpi = (int)l;
                break;
            case double d:
                dpi = (int)Math.Round(d);
                break;
            case float f:
                dpi = (int)Math.Round(f);
                break;
            case string s when int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p):
                dpi = p;
                break;
            default:
                if (int.TryParse(v.ToString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var any))
                    dpi = any;
                break;
        }

        return Math.Clamp(dpi, 72, 300);
    }

    private static string ParseZipInnerFormatFromConfig(IReadOnlyDictionary<string, object?> config)
    {
        if (!config.TryGetValue("ZipImageFormat", out var v) || v is null)
            return "png";

        var s = v.ToString()?.Trim().ToLowerInvariant();
        return s is "jpg" or "jpeg" ? "jpg" : "png";
    }

    private static async Task OutputAsZipAsync(
        ExecuteContext context,
        List<SKBitmap> images,
        SKEncodedImageFormat format,
        int quality,
        string ext,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(context.TempJobDir, "output.zip");

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            for (var i = 0; i < images.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryName = $"output_{(i + 1):D3}.{ext}";
                var entry = archive.CreateEntry(entryName);

                using var entryStream = entry.Open();
                using var skStream = new SKManagedWStream(entryStream);
                images[i].Encode(skStream, format, quality);
            }
        }, cancellationToken);

        reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
        reporter.OnCompleted(new CompletedInfo("output.zip", OutputSuggestedExt: "zip"));
        reporter.OnLog($"[pdf2img] Created ZIP with {images.Count} images");
    }

    private static async Task OutputAsLongImageAsync(
        ExecuteContext context,
        List<SKBitmap> images,
        SKEncodedImageFormat format,
        int quality,
        string ext,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        if (images.Count == 0)
        {
            reporter.OnFailed(new FailedInfo("No pages to render", "NO_PAGES"));
            return;
        }

        var totalHeight = images.Sum(img => img.Height);
        var maxWidth = images.Max(img => img.Width);

        reporter.OnLog($"[pdf2img] Creating long image: {maxWidth}x{totalHeight}");

        using var longBitmap = new SKBitmap(maxWidth, totalHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(longBitmap);
        canvas.Clear(SKColors.White);

        var yOffset = 0;
        foreach (var img in images)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var xOffset = (maxWidth - img.Width) / 2;
            canvas.DrawBitmap(img, xOffset, yOffset);
            yOffset += img.Height;
        }

        var outputPath = Path.Combine(context.TempJobDir, $"output.{ext}");
        await Task.Run(() =>
        {
            using var fileStream = File.Create(outputPath);
            using var skStream = new SKManagedWStream(fileStream);
            longBitmap.Encode(skStream, format, quality);
        }, cancellationToken);

        reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
        reporter.OnCompleted(new CompletedInfo($"output.{ext}", OutputSuggestedExt: ext));
        reporter.OnLog($"[pdf2img] Created long image: {outputPath}");
    }
}
