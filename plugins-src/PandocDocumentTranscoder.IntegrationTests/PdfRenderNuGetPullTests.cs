using PluginAbstractions;
using Xunit;

namespace PandocDocumentTranscoder.IntegrationTests;

/// <summary>Verifies api.nuget.org flat-container downloads + extract match plugin runtime expectations.</summary>
public sealed class PdfRenderNuGetPullTests
{
    private sealed class NoopReporter : IProgressReporter
    {
        public void OnCompleted(CompletedInfo info) { }
        public void OnFailed(FailedInfo info) { }
        public void OnLog(string line) { }
        public void OnProgress(ProgressInfo info) { }
    }

    private static string ResolvePandocPluginBin()
    {
        // Base: .../PandocDocumentTranscoder.IntegrationTests/bin/<cfg>/net8.0/
        var pluginsSrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        foreach (var cfg in new[] { "Release", "Debug" })
        {
            var p = Path.Combine(pluginsSrc, "PandocDocumentTranscoder", "bin", cfg, "net8.0");
            if (File.Exists(Path.Combine(p, "PandocDocumentTranscoder.PdfRender.dll")))
                return p;
        }

        throw new InvalidOperationException(
            "Build plugins-src/PandocDocumentTranscoder first (need PandocDocumentTranscoder.PdfRender.dll next to main plugin output).");
    }

    [Fact]
    public async Task PdfRenderNuGetCache_PullAsync_then_shim_copy_yields_complete_cache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ct-pdf-nuget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await PdfRenderNuGetCache.PullAsync(dir, new NoopReporter(), CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(dir, "pdfium.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "libSkiaSharp.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "DtronixPdf.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "PDFiumCore.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "SkiaSharp.dll")));

            var pluginBin = ResolvePandocPluginBin();
            foreach (var name in new[] { "PandocDocumentTranscoder.PdfRender.dll", "PandocDocumentTranscoder.PdfRender.deps.json", "PluginAbstractions.dll" })
            {
                var src = Path.Combine(pluginBin, name);
                Assert.True(File.Exists(src), $"Missing {src}");
                File.Copy(src, Path.Combine(dir, name), overwrite: true);
            }

            Assert.True(PdfRenderNuGetCache.IsComplete(dir));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore cleanup on locked AV scans
            }
        }
    }
}
