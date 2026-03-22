using PluginAbstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DtronixPdf;
using SkiaSharp;

namespace PandocDocumentTranscoder;

public sealed class PandocDocumentTranscoderPlugin : IConverterPlugin
{
    private const string PandocExeName = "pandoc.exe";
    private const string LibreOfficeExeName = "soffice.exe";

    private static readonly string PandocVersion = "3.6.4";
    private static readonly string LibreOfficeVersion = "25.8.5";

    private static readonly string PandocDownloadUrl =
        $"https://github.com/jgm/pandoc/releases/download/{PandocVersion}/pandoc-{PandocVersion}-windows-x86_64.zip";

    private static readonly string LibreOfficeDownloadUrl =
        $"https://mirrors.tuna.tsinghua.edu.cn/libreoffice/libreoffice/stable/{LibreOfficeVersion}/win/x86_64/LibreOffice_{LibreOfficeVersion}_Win_x86-64.msi";

    private static readonly SemaphoreSlim ToolEnsureGate = new(1, 1);

    public PluginManifest GetManifest()
        => new PluginManifest(
            "pandoc.document.transcoder",
            "1.1.0",
            new[]
            {
                "md", "markdown", "docx", "odt", "html", "htm", "epub", "rst", "txt", "latex", "tex",
                "docbook", "org", "ipynb", "csv", "tsv", "fb2", "typst", "doc", "ppt", "pptx", "pdf"
            },
            new[]
            {
                new TargetFormat("docx", "plugin/pandoc.document.transcoder/target/docx", null),
                new TargetFormat("doc", "plugin/pandoc.document.transcoder/target/doc", null),
                new TargetFormat("odt", "plugin/pandoc.document.transcoder/target/odt", null),
                new TargetFormat("html", "plugin/pandoc.document.transcoder/target/html", null),
                new TargetFormat("epub", "plugin/pandoc.document.transcoder/target/epub", null),
                new TargetFormat("md", "plugin/pandoc.document.transcoder/target/md", null),
                new TargetFormat("txt", "plugin/pandoc.document.transcoder/target/txt", null),
                new TargetFormat("rst", "plugin/pandoc.document.transcoder/target/rst", null),
                new TargetFormat("typst", "plugin/pandoc.document.transcoder/target/typst", null),
                new TargetFormat("pdf", "plugin/pandoc.document.transcoder/target/pdf", null),
                new TargetFormat("png", "plugin/pandoc.document.transcoder/target/pngLong", null),
                new TargetFormat("jpg", "plugin/pandoc.document.transcoder/target/jpgLong", null),
                new TargetFormat("zip", "plugin/pandoc.document.transcoder/target/zip", null),
            },
            new ConfigSchema(Array.Empty<ConfigSection>()),
            new[] { "en-US", "zh-CN" },
            new I18nDescriptor("locales")
        );

    public async Task ExecuteAsync(
        ExecuteContext context,
        IProgressReporter reporter,
        CancellationToken cancellationToken = default)
    {
        reporter.OnProgress(new ProgressInfo(ProgressStage.Preparing, 0));

        var pluginDir = GetPluginDir();
        Directory.CreateDirectory(context.TempJobDir);

        var inputPath = context.InputPath;
        if (!File.Exists(inputPath))
        {
            reporter.OnFailed(new FailedInfo($"Input file not found: {inputPath}", "INPUT_NOT_FOUND"));
            return;
        }

        var targetExt = (context.TargetFormatId ?? "docx").Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(targetExt))
            targetExt = "docx";

        var inputExt = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var actualInputPath = inputPath;
        var tempDocxPath = (string?)null;

        if (inputExt == "doc")
        {
            reporter.OnLog("[converter] detected .doc file, converting to .docx first...");
            var libreOfficePath = await EnsureLibreOfficeAsync(reporter, cancellationToken);
            tempDocxPath = Path.Combine(context.TempJobDir, "temp_input.docx");
            await ConvertDocToDocxAsync(inputPath, tempDocxPath, libreOfficePath, reporter, cancellationToken);
            actualInputPath = tempDocxPath;
            inputExt = "docx";
        }

        if (inputExt is "ppt" or "pptx")
        {
            reporter.OnLog("[converter] detected PowerPoint file, converting to PDF...");
            var libreOfficePath = await EnsureLibreOfficeAsync(reporter, cancellationToken);

            var outputRelative = "output.pdf";
            var outputPath = Path.Combine(context.TempJobDir, outputRelative);
            await ConvertToPdfAsync(inputPath, outputPath, libreOfficePath, reporter, cancellationToken);

            reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
            reporter.OnCompleted(new CompletedInfo(outputRelative, OutputSuggestedExt: "pdf"));
            return;
        }

        var pdfTarget = NormalizePdfImageTargetId(targetExt);
        if (IsPdfDocument(context.InputPath, inputExt) && pdfTarget is not null)
        {
            reporter.OnLog($"[pdf2img] PDF → {pdfTarget} via DtronixPdf (no Ghostscript; not Pandoc)...");
            await ConvertPdfToImagesAsync(context, pdfTarget, reporter, cancellationToken);
            return;
        }

        if (targetExt == "pdf")
        {
            reporter.OnLog("[converter] PDF output requested, using LibreOffice...");
            var pandocPath = await EnsurePandocAsync(reporter, cancellationToken);
            var libreOfficePath = await EnsureLibreOfficeAsync(reporter, cancellationToken);

            var tempDocxPath2 = Path.Combine(context.TempJobDir, "temp_output.docx");
            var fromFormat = MapInputFormat(inputExt);
            var args = BuildPandocArgs(actualInputPath, tempDocxPath2, fromFormat, "docx");
            reporter.OnLog("[pandoc] args: " + string.Join(" ", args.Select(QuoteIfNeeded)));

            var (exitCode, stderr) = await RunPandocAsync(pandocPath, args, reporter, cancellationToken);
            if (exitCode != 0)
            {
                reporter.OnFailed(new FailedInfo($"pandoc failed (exit={exitCode}). {stderr}", "PANDOC_FAILED"));
                return;
            }

            var outputRelative = "output.pdf";
            var outputPath = Path.Combine(context.TempJobDir, outputRelative);
            await ConvertToPdfAsync(tempDocxPath2, outputPath, libreOfficePath, reporter, cancellationToken);

            reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
            reporter.OnCompleted(new CompletedInfo(outputRelative, OutputSuggestedExt: "pdf"));
            return;
        }

        if (targetExt == "doc")
        {
            reporter.OnLog("[converter] DOC output requested, using LibreOffice...");
            var pandocPath = await EnsurePandocAsync(reporter, cancellationToken);
            var libreOfficePath = await EnsureLibreOfficeAsync(reporter, cancellationToken);

            var tempDocxPath3 = Path.Combine(context.TempJobDir, "temp_output.docx");
            var fromFormat3 = MapInputFormat(inputExt);
            var args3 = BuildPandocArgs(actualInputPath, tempDocxPath3, fromFormat3, "docx");
            reporter.OnLog("[pandoc] args: " + string.Join(" ", args3.Select(QuoteIfNeeded)));

            var (exitCode3, stderr3) = await RunPandocAsync(pandocPath, args3, reporter, cancellationToken);
            if (exitCode3 != 0)
            {
                reporter.OnFailed(new FailedInfo($"pandoc failed (exit={exitCode3}). {stderr3}", "PANDOC_FAILED"));
                return;
            }

            var outputRelative3 = "output.doc";
            var outputPath3 = Path.Combine(context.TempJobDir, outputRelative3);
            await ConvertDocxToDocAsync(tempDocxPath3, outputPath3, libreOfficePath, reporter, cancellationToken);

            reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
            reporter.OnCompleted(new CompletedInfo(outputRelative3, OutputSuggestedExt: "doc"));
            return;
        }

        var pandocPath2 = await EnsurePandocAsync(reporter, cancellationToken);
        var fromFormat2 = MapInputFormat(inputExt);
        var toFormat = MapOutputFormat(targetExt);

        var outputRelative2 = $"output.{targetExt}";
        var outputPath2 = Path.Combine(context.TempJobDir, outputRelative2);

        reporter.OnLog($"[pandoc] input={actualInputPath}");
        reporter.OnLog($"[pandoc] output={outputPath2}");
        reporter.OnLog($"[pandoc] from={fromFormat2}, to={toFormat}");

        reporter.OnProgress(new ProgressInfo(ProgressStage.Preparing, 50));

        var args2 = BuildPandocArgs(actualInputPath, outputPath2, fromFormat2, toFormat);
        reporter.OnLog("[pandoc] args: " + string.Join(" ", args2.Select(QuoteIfNeeded)));

        reporter.OnProgress(new ProgressInfo(ProgressStage.Running, 0));

        var (exitCode2, stderr2) = await RunPandocAsync(pandocPath2, args2, reporter, cancellationToken);

        if (exitCode2 != 0)
        {
            reporter.OnFailed(new FailedInfo($"pandoc failed (exit={exitCode2}). {stderr2}", "PANDOC_FAILED"));
            return;
        }

        if (!File.Exists(outputPath2))
        {
            var possibleOutputs = Directory.GetFiles(context.TempJobDir, "*.*", SearchOption.TopDirectoryOnly);
            if (possibleOutputs.Length > 0)
            {
                var found = possibleOutputs.FirstOrDefault(f => !Path.GetFileName(f).Equals(Path.GetFileName(actualInputPath), StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    outputRelative2 = Path.GetFileName(found);
                    outputPath2 = found;
                }
                else
                {
                    reporter.OnFailed(new FailedInfo("pandoc completed but output file not found.", "OUTPUT_MISSING"));
                    return;
                }
            }
            else
            {
                reporter.OnFailed(new FailedInfo("pandoc completed but output file not found.", "OUTPUT_MISSING"));
                return;
            }
        }

        reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
        reporter.OnCompleted(new CompletedInfo(outputRelative2, OutputSuggestedExt: targetExt));
    }

    private static string GetPluginDir()
    {
        var loc = typeof(PandocDocumentTranscoderPlugin).Assembly.Location;
        return Path.GetDirectoryName(loc) ?? AppContext.BaseDirectory;
    }

    private static string MapInputFormat(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            "md" or "markdown" => "markdown",
            "docx" => "docx",
            "odt" => "odt",
            "html" or "htm" => "html",
            "epub" => "epub",
            "rst" => "rst",
            "txt" => "plain",
            "latex" or "tex" => "latex",
            "docbook" => "docbook",
            "org" => "org",
            "ipynb" => "ipynb",
            "csv" => "csv",
            "tsv" => "tsv",
            "fb2" => "fb2",
            "typst" => "typst",
            _ => ext
        };
    }

    private static string MapOutputFormat(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            "md" => "markdown",
            "docx" => "docx",
            "odt" => "odt",
            "html" => "html",
            "epub" => "epub",
            "rst" => "rst",
            "txt" => "plain",
            "typst" => "typst",
            _ => ext
        };
    }

    private static string[] BuildPandocArgs(string inputPath, string outputPath, string fromFormat, string toFormat)
    {
        var args = new List<string>
        {
            "-f", fromFormat,
            "-t", toFormat,
            "-o", outputPath,
            inputPath
        };

        return args.ToArray();
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\t'))
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
        return arg;
    }

    private static async Task<(int exitCode, string stderr)> RunPandocAsync(
        string pandocPath,
        string[] args,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<int>();

        var psi = new ProcessStartInfo
        {
            FileName = pandocPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(pandocPath)
        };

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var stderrBuilder = new StringBuilder();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                stderrBuilder.AppendLine(e.Data);
                reporter.OnLog("[pandoc stderr] " + e.Data);
            }
        };

        process.Exited += (s, e) =>
        {
            tcs.TrySetResult(process.ExitCode);
        };

        process.Start();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch { }
            tcs.TrySetCanceled();
        });

        var exitCode = await tcs.Task;

        return (exitCode, stderrBuilder.ToString());
    }

    private static async Task<string> EnsurePandocAsync(
        IProgressReporter reporter,
        CancellationToken ct)
    {
        var fromPath = TryGetFromPath(PandocExeName);
        if (fromPath is not null)
        {
            reporter.OnLog($"[pandoc] using from PATH: {fromPath}");
            return fromPath;
        }

        var cached = SharedToolCache.GetToolPath("pandoc", PandocExeName, $"pandoc-{PandocVersion}-windows-x86_64");
        if (File.Exists(cached))
        {
            reporter.OnLog($"[pandoc] using cached: {cached}");
            return cached;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new InvalidOperationException("pandoc not found. Auto-download is implemented for Windows only.");
        }

        await ToolEnsureGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(cached))
            {
                reporter.OnLog($"[pandoc] using cached: {cached}");
                return cached;
            }

            reporter.OnLog("[pandoc] downloading Pandoc...");

            await SharedToolCache.DownloadAndExtractAsync(
                "pandoc",
                $"pandoc-{PandocVersion}-windows-x86_64",
                PandocDownloadUrl,
                SharedToolCache.GetToolDir("pandoc", $"pandoc-{PandocVersion}-windows-x86_64"),
                reporter,
                ct);

            if (!File.Exists(cached))
            {
                throw new InvalidOperationException("Pandoc downloaded but executable not found.");
            }

            reporter.OnLog($"[pandoc] download OK: {cached}");
            return cached;
        }
        finally
        {
            ToolEnsureGate.Release();
        }
    }

    private static async Task<string> EnsureLibreOfficeAsync(
        IProgressReporter reporter,
        CancellationToken ct)
    {
        var fromPath = TryGetFromPath(LibreOfficeExeName);
        if (fromPath is not null)
        {
            reporter.OnLog($"[libreoffice] using from PATH: {fromPath}");
            return fromPath;
        }

        var extractDir = SharedToolCache.GetToolDir("libreoffice", LibreOfficeVersion);
        var cached = Path.Combine(extractDir, "program", LibreOfficeExeName);
        if (File.Exists(cached))
        {
            reporter.OnLog($"[libreoffice] using cached: {cached}");
            return cached;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new InvalidOperationException("LibreOffice not found. Auto-download is implemented for Windows only.");
        }

        await ToolEnsureGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(cached))
            {
                reporter.OnLog($"[libreoffice] using cached: {cached}");
                return cached;
            }

            reporter.OnLog("[libreoffice] downloading LibreOffice MSI (~350MB)...");

            var msiPath = Path.Combine(SharedToolCache.CacheRoot, $"libreoffice-{LibreOfficeVersion}.msi");

            Directory.CreateDirectory(SharedToolCache.CacheRoot);

            using var hc = new HttpClient();
            hc.Timeout = TimeSpan.FromMinutes(60);
            hc.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ConverTool/1.1");
            
            using (var response = await hc.GetAsync(LibreOfficeDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                
                await using (var fs = File.Create(msiPath))
                await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                {
                    await stream.CopyToAsync(fs, 81920, ct).ConfigureAwait(false);
                }
            }

            reporter.OnLog("[libreoffice] extracting MSI (this may take a while)...");

            await Task.Run(() =>
            {
                Directory.CreateDirectory(extractDir);
                
                var psi = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/a \"{msiPath}\" /qn TARGETDIR=\"{extractDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                process?.WaitForExit();
            }, ct).ConfigureAwait(false);

            try { File.Delete(msiPath); } catch { }

            if (!File.Exists(cached))
            {
                var altPath = Directory.GetFiles(extractDir, LibreOfficeExeName, SearchOption.AllDirectories).FirstOrDefault();
                if (altPath != null)
                {
                    cached = altPath;
                }
                else
                {
                    throw new InvalidOperationException("LibreOffice downloaded but executable not found.");
                }
            }

            reporter.OnLog($"[libreoffice] download OK: {cached}");
            return cached;
        }
        finally
        {
            ToolEnsureGate.Release();
        }
    }

    private static async Task ConvertDocToDocxAsync(
        string inputPath,
        string outputPath,
        string libreOfficePath,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var outputDir = Path.GetDirectoryName(outputPath)!;
        var psi = new ProcessStartInfo
        {
            FileName = libreOfficePath,
            Arguments = $"--headless --convert-to docx --outdir \"{outputDir}\" \"{inputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        reporter.OnLog($"[libreoffice] converting {inputPath} to docx...");

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start LibreOffice.");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
        });

        await process.WaitForExitAsync(cancellationToken);

        var expectedOutput = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputPath) + ".docx");
        if (File.Exists(expectedOutput) && expectedOutput != outputPath)
        {
            File.Move(expectedOutput, outputPath, overwrite: true);
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException($"LibreOffice conversion failed. Output not found: {outputPath}");
        }

        reporter.OnLog($"[libreoffice] conversion complete: {outputPath}");
    }

    private static async Task ConvertToPdfAsync(
        string inputPath,
        string outputPath,
        string libreOfficePath,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var outputDir = Path.GetDirectoryName(outputPath)!;
        var psi = new ProcessStartInfo
        {
            FileName = libreOfficePath,
            Arguments = $"--headless --convert-to pdf --outdir \"{outputDir}\" \"{inputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        reporter.OnLog($"[libreoffice] converting {inputPath} to PDF...");

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start LibreOffice.");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
        });

        await process.WaitForExitAsync(cancellationToken);

        var expectedOutput = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
        if (File.Exists(expectedOutput) && expectedOutput != outputPath)
        {
            File.Move(expectedOutput, outputPath, overwrite: true);
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException($"LibreOffice PDF conversion failed. Output not found: {outputPath}");
        }

        reporter.OnLog($"[libreoffice] PDF conversion complete: {outputPath}");
    }

    private static async Task ConvertDocxToDocAsync(
        string inputPath,
        string outputPath,
        string libreOfficePath,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var outputDir = Path.GetDirectoryName(outputPath)!;
        var psi = new ProcessStartInfo
        {
            FileName = libreOfficePath,
            Arguments = $"--headless --convert-to doc --outdir \"{outputDir}\" \"{inputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        reporter.OnLog($"[libreoffice] converting {inputPath} to DOC...");

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start LibreOffice.");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
        });

        await process.WaitForExitAsync(cancellationToken);

        var expectedOutput = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputPath) + ".doc");
        if (File.Exists(expectedOutput) && expectedOutput != outputPath)
        {
            File.Move(expectedOutput, outputPath, overwrite: true);
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException($"LibreOffice DOC conversion failed. Output not found: {outputPath}");
        }

        reporter.OnLog($"[libreoffice] DOC conversion complete: {outputPath}");
    }

    private static string? TryGetFromPath(string exeName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';

        foreach (var segment in pathEnv.Split(separator))
        {
            try
            {
                var trimmed = segment.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var candidate = Path.Combine(trimmed, exeName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch { }
        }

        return null;
    }

    /// <summary>PDF→image targets: png/jpg (long) or zip (multi-page archive).</summary>
    private static string? NormalizePdfImageTargetId(string targetExt)
    {
        if (string.IsNullOrWhiteSpace(targetExt))
            return null;

        switch (targetExt.Trim().TrimStart('.').ToLowerInvariant())
        {
            case "png":
                return "png";
            case "jpg":
            case "jpeg":
                return "jpg";
            case "zip":
                return "zip";
            default:
                return null;
        }
    }

    private static bool IsPdfDocument(string path, string extLower)
    {
        if (string.Equals(extLower, "pdf", StringComparison.OrdinalIgnoreCase))
            return true;

        return FileStartsWithPdfMagic(path);
    }

    private static bool FileStartsWithPdfMagic(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> buf = stackalloc byte[5];
            if (fs.Read(buf) < 4)
                return false;

            return buf[0] == (byte)'%' && buf[1] == (byte)'P' && buf[2] == (byte)'D' && buf[3] == (byte)'F';
        }
        catch
        {
            return false;
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

    /// <summary>PNG or JPG for pages inside the ZIP when target format is <c>zip</c>.</summary>
    private static string ParseZipInnerFormatFromConfig(IReadOnlyDictionary<string, object?> config)
    {
        if (!config.TryGetValue("ZipImageFormat", out var v) || v is null)
            return "png";

        var s = v.ToString()?.Trim().ToLowerInvariant();
        return s is "jpg" or "jpeg" ? "jpg" : "png";
    }

    private static async Task ConvertPdfToImagesAsync(
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
