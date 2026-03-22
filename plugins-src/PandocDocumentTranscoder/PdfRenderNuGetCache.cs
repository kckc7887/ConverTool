using System.IO.Compression;
using System.Net.Http;
using PluginAbstractions;

namespace PandocDocumentTranscoder;

/// <summary>Pulls PDF→image native/managed deps from NuGet.org (flat container) into SharedToolCache — no separate Release zip.</summary>
internal static class PdfRenderNuGetCache
{
    /// <summary>Must stay in sync with PandocDocumentTranscoder.PdfRender.csproj package versions.</summary>
    private static readonly (string Id, string Version)[] Packages =
    [
        ("DtronixPdf", "1.3.1"),
        ("PDFiumCore", "134.0.6982"),
        ("bblanchon.PDFium.Win32", "134.0.6982"),
        ("SkiaSharp", "3.116.1"),
        ("SkiaSharp.NativeAssets.Win32", "3.116.1"),
    ];

    private static readonly string[] RequiredDllsAfterPull =
    [
        "pdfium.dll",
        "libSkiaSharp.dll",
        "DtronixPdf.dll",
        "PDFiumCore.dll",
        "SkiaSharp.dll",
    ];

    internal static bool IsComplete(string dir)
    {
        foreach (var r in RequiredDllsAfterPull)
        {
            if (!File.Exists(Path.Combine(dir, r)))
                return false;
        }

        return File.Exists(Path.Combine(dir, "PandocDocumentTranscoder.PdfRender.dll"))
               && File.Exists(Path.Combine(dir, "PluginAbstractions.dll"));
    }

    internal static async Task PullAsync(string destDir, IProgressReporter reporter, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);

        using var hc = new HttpClient();
        hc.Timeout = TimeSpan.FromMinutes(20);
        hc.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ConverTool PandocDocumentTranscoder (PDF render)");

        foreach (var (id, version) in Packages)
        {
            ct.ThrowIfCancellationRequested();
            var url = BuildFlatContainerUrl(id, version);
            reporter.OnLog($"[pdf2img] nuget.org: {id} {version}");

            using var resp = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"NuGet download failed ({(int)resp.StatusCode}): {id} {version}. URL: {url}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            ExtractPackageDlls(zip, destDir);
        }
    }

    private static string BuildFlatContainerUrl(string packageId, string version)
    {
        var idLower = packageId.ToLowerInvariant();
        return $"https://api.nuget.org/v3-flatcontainer/{idLower}/{version}/{idLower}.{version}.nupkg";
    }

    private static void ExtractPackageDlls(ZipArchive zip, string destDir)
    {
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var full = entry.FullName.Replace('\\', '/');
            if (full.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (full.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                continue;
            if (full.StartsWith("package/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            string? destName = null;

            // Zip entry paths use forward slashes and no leading slash (e.g. lib/net8.0/foo.dll).
            if (full.Contains("lib/net8.0/", StringComparison.OrdinalIgnoreCase))
                destName = Path.GetFileName(full);
            else if (full.Contains("lib/net6.0/", StringComparison.OrdinalIgnoreCase))
                destName = Path.GetFileName(full);
            else if (full.Contains("lib/netstandard2.1/", StringComparison.OrdinalIgnoreCase))
                destName = Path.GetFileName(full);
            else if (full.Contains("lib/netstandard2.0/", StringComparison.OrdinalIgnoreCase))
                destName = Path.GetFileName(full);
            else if (full.Contains("runtimes/win-x64/native/", StringComparison.OrdinalIgnoreCase))
                destName = Path.GetFileName(full);

            if (destName is null)
                continue;

            var destPath = Path.Combine(destDir, destName);
            using (var inStream = entry.Open())
            using (var outStream = File.Create(destPath))
            {
                inStream.CopyTo(outStream);
            }
        }
    }
}
