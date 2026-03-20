using PluginAbstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ImageMagickImageTranscoder;

public sealed class ImageMagickImageTranscoderPlugin : IConverterPlugin
{
    private const string MagickExeName = "magick.exe";

    // Cached under user's profile; avoids requiring admin install.
    private static readonly string CacheRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConverTool", "imagemagick");

    // Portable archive url provided by ImageMagick official download page.
    // Notes:
    // - This is a .7z archive; we extract it using SharpCompress.
    // - If download/extract fails, we fall back to returning a clear error.
    private static readonly string ImVersionKey = "7.1.2-17-portable-Q16-x64";
    private static readonly string ImInstallDir = Path.Combine(CacheRoot, ImVersionKey);
    // Some environments may block/return 404 for `imagemagick.org/download/*`.
    // We try multiple official/known mirrors in order and use the first that succeeds.
    private static readonly string[] ImDownloadUrls =
    {
        // (current, may fail with 404 in some networks)
        "https://imagemagick.org/download/ImageMagick-7.1.2-17-portable-Q16-x64.7z",

        // official archive host
        "https://download.imagemagick.org/archive/binaries/ImageMagick-7.1.2-17-portable-Q16-x64.7z",
        "https://download.imagemagick.org/archive/binaries/ImageMagick-7.1.2-17-portable-Q16-HDRI-x64.7z",
        "https://download.imagemagick.org/archive/binaries/ImageMagick-7.1.2-17-portable-Q8-x64.7z",

        // GitHub release asset direct link (official repository)
        "https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-17/ImageMagick-7.1.2-17-portable-Q16-x64.7z",
        "https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-17/ImageMagick-7.1.2-17-portable-Q16-HDRI-x64.7z",
        "https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-17/ImageMagick-7.1.2-17-portable-Q8-x64.7z",
    };
    private static readonly string ImArchivePath = Path.Combine(ImInstallDir, "imagemagick.7z");

    // 7-Zip extractor for .7z archives.
    // Requirement: "official" download, so we use 7-zip.org's official `7zr.exe` console extractor.
    private static readonly string SevenZipExeName = "7zr.exe";
    private static readonly string SevenZipInstallDir =
        Path.Combine(CacheRoot, "7zip");
    private static readonly string SevenZipExePath =
        Path.Combine(SevenZipInstallDir, SevenZipExeName);
    private static readonly string SevenZipDownloadUrl =
        "https://www.7-zip.org/a/7zr.exe";

    public PluginManifest GetManifest()
        => new PluginManifest(
            "imagemagick.image.transcoder",
            "0.1.0",
            new[]
            {
                "png", "jpg", "jpeg", "bmp", "gif", "tif", "tiff", "webp", "ico", "avif", "heic"
            },
            new[]
            {
                new TargetFormat("png", "plugin/imagemagick.image.transcoder/target/png", null),
                new TargetFormat("jpg", "plugin/imagemagick.image.transcoder/target/jpg", null),
                new TargetFormat("jpeg", "plugin/imagemagick.image.transcoder/target/jpeg", null),
                new TargetFormat("bmp", "plugin/imagemagick.image.transcoder/target/bmp", null),
                new TargetFormat("gif", "plugin/imagemagick.image.transcoder/target/gif", null),
                new TargetFormat("tif", "plugin/imagemagick.image.transcoder/target/tif", null),
                new TargetFormat("tiff", "plugin/imagemagick.image.transcoder/target/tiff", null),
                new TargetFormat("webp", "plugin/imagemagick.image.transcoder/target/webp", null),
                new TargetFormat("avif", "plugin/imagemagick.image.transcoder/target/avif", null),
                new TargetFormat("ico", "plugin/imagemagick.image.transcoder/target/ico", null)
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
        Directory.CreateDirectory(context.TempJobDir);

        var inputPath = context.InputPath;
        if (!File.Exists(inputPath))
        {
            reporter.OnFailed(new FailedInfo($"Input file not found: {inputPath}", "INPUT_NOT_FOUND"));
            return;
        }

        var targetExt = (context.TargetFormatId ?? "png").Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(targetExt))
            targetExt = "png";

        // Use extension to let ImageMagick infer output format.
        var outputRelative = $"output.{targetExt}";
        var outputPath = Path.Combine(context.TempJobDir, outputRelative);

        static string? GetString(IReadOnlyDictionary<string, object?> dict, string key)
        {
            if (!dict.TryGetValue(key, out var v) || v is null)
            {
                return null;
            }
            return v switch
            {
                string s => s,
                _ => v.ToString()
            };
        }

        static bool GetBool(IReadOnlyDictionary<string, object?> dict, string key, bool defaultValue)
        {
            if (!dict.TryGetValue(key, out var v) || v is null)
            {
                return defaultValue;
            }

            if (v is bool b)
            {
                return b;
            }

            var s = v.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                return defaultValue;
            }

            return bool.TryParse(s, out var parsed) ? parsed : defaultValue;
        }

        static decimal GetDecimal(IReadOnlyDictionary<string, object?> dict, string key, decimal defaultValue)
        {
            if (!dict.TryGetValue(key, out var v) || v is null)
            {
                return defaultValue;
            }

            var s = v.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                return defaultValue;
            }

            // Be forgiving for locale separators.
            s = s.Replace(',', '.');
            return decimal.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        // ImageMagick target-size compression options (driven by Host config schema).
        var enableTargetSizeCompression = GetBool(context.SelectedConfig, "enableTargetSizeCompression", defaultValue: false);
        var retainTargetSizeCompressionSettings = GetBool(context.SelectedConfig, "retainTargetSizeCompressionSettings", defaultValue: false);
        var targetSizeUnit = GetString(context.SelectedConfig, "targetSizeUnit")?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(targetSizeUnit))
        {
            targetSizeUnit = "KB";
        }

        var targetSizeMinValue = GetDecimal(context.SelectedConfig, "targetSizeMinKb", defaultValue: 0);
        var targetSizeMaxValue = GetDecimal(context.SelectedConfig, "targetSizeMaxKb", defaultValue: 0);

        static bool IsLosslessTarget(string ext)
            => ext is "png" or "bmp" or "gif" or "tiff" or "tif";

        // Lossless targets are explicitly disallowed when target-size compression is enabled.
        if (enableTargetSizeCompression && IsLosslessTarget(targetExt))
        {
            reporter.OnFailed(new FailedInfo(
                "Target size compression is enabled, but selected a lossless target format (PNG/BMP/GIF/TIFF). Please disable target size compression or choose a lossy format.",
                "TARGET_SIZE_COMPRESSION_LOSSLESS_NOT_ALLOWED"));
            return;
        }

        reporter.OnLog($"[imagemagick] input={inputPath}");
        reporter.OnLog($"[imagemagick] output={outputPath}");

        var magickPath = await EnsureMagickAsync(reporter, cancellationToken).ConfigureAwait(false);

        var lastLines = new Queue<string>(capacity: 80);
        var sync = new object();

        void HandleLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            reporter.OnLog("[imagemagick] " + line);

            lock (sync)
            {
                if (lastLines.Count >= 80) lastLines.Dequeue();
                lastLines.Enqueue(line);
            }
        }

        // Stable PATH for portable ImageMagick.
        var magickDir = Path.GetDirectoryName(magickPath);
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var effectivePath = !string.IsNullOrWhiteSpace(magickDir) ? magickDir + ";" + currentPath : currentPath;

        async Task<(bool Ok, long? SizeBytes, string? ErrorMessage)> RunEncodeAsync(int? quality, string outPath)
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = magickPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(magickPath) ?? Environment.CurrentDirectory
            };

            if (!string.IsNullOrWhiteSpace(effectivePath))
            {
                p.StartInfo.EnvironmentVariables["PATH"] = effectivePath;
            }

            // magick <input> [options] <output>
            p.StartInfo.ArgumentList.Add(inputPath);

            if (quality is not null)
            {
                // -quality is accepted by common lossy coders in ImageMagick.
                p.StartInfo.ArgumentList.Add("-quality");
                p.StartInfo.ArgumentList.Add(quality.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            // ICO has strict size limits. Constrain source and emit common icon sizes
            // to avoid "width or height exceeds limit" for large images.
            if (string.Equals(targetExt, "ico", StringComparison.OrdinalIgnoreCase))
            {
                p.StartInfo.ArgumentList.Add("-resize");
                p.StartInfo.ArgumentList.Add("256x256>");
                p.StartInfo.ArgumentList.Add("-define");
                p.StartInfo.ArgumentList.Add("icon:auto-resize=256,128,64,48,32,16");
            }

            p.StartInfo.ArgumentList.Add(outPath);

            p.OutputDataReceived += (_, e) => HandleLine(e.Data);
            p.ErrorDataReceived += (_, e) => HandleLine(e.Data);

            using var _ = cancellationToken.Register(() =>
            {
                try
                {
                    if (!p.HasExited)
                        p.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best effort
                }
            });

            try
            {
                reporter.OnProgress(new ProgressInfo(ProgressStage.Preparing, 100));
                reporter.OnProgress(new ProgressInfo(ProgressStage.Running, 0));

                reporter.OnLog($"[imagemagick] exec: {Path.GetFileName(magickPath)} q={(quality.HasValue ? quality.Value.ToString() : "-") } ext={targetExt}");

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
            }
            catch (OperationCanceledException)
            {
                return (Ok: false, SizeBytes: null, ErrorMessage: "Canceled.");
            }
            catch (Exception ex)
            {
                return (Ok: false, SizeBytes: null, ErrorMessage: $"Failed to run ImageMagick: {ex.Message}");
            }

            if (p.ExitCode != 0)
            {
                string tail;
                lock (sync)
                {
                    tail = string.Join(Environment.NewLine, lastLines);
                }
                return (Ok: false, SizeBytes: null, ErrorMessage: $"ImageMagick failed (exit={p.ExitCode}).\n{tail}");
            }

            if (!File.Exists(outPath))
            {
                return (Ok: false, SizeBytes: null, ErrorMessage: "ImageMagick completed but output file not found.");
            }

            return (Ok: true, SizeBytes: new FileInfo(outPath).Length, ErrorMessage: null);
        }

        // Target-size compression search (binary search over quality).
        if (enableTargetSizeCompression &&
            (targetExt is "jpg" or "jpeg" or "webp" or "avif"))
        {
            const long kbBytes = 1024;
            const long mbBytes = 1024 * 1024;

            long minBytes = targetSizeMinValue <= 0 ? 0 : (long)(targetSizeUnit == "MB" ? targetSizeMinValue * mbBytes : targetSizeMinValue * kbBytes);
            long maxBytes = targetSizeMaxValue <= 0 ? long.MaxValue : (long)(targetSizeUnit == "MB" ? targetSizeMaxValue * mbBytes : targetSizeMaxValue * kbBytes);

            if (maxBytes < minBytes)
            {
                (minBytes, maxBytes) = (maxBytes, minBytes);
            }

            // Cache key: target format + range.
            var cacheKey = $"{targetExt}|{minBytes}|{maxBytes}";
            int? cachedQuality = null;
            if (retainTargetSizeCompressionSettings)
            {
                if (_qualityCache.TryGetValue(cacheKey, out var q))
                {
                    cachedQuality = q;
                }
            }

            reporter.OnLog($"[imagemagick] target-size q-search unit={targetSizeUnit} minBytes={minBytes} maxBytes={maxBytes}");

            var trialBest = (Quality: (int?)null, Size: (long?)null, Dist: long.MaxValue);
            string? lastError = null;

            // First: try cached quality if available.
            if (cachedQuality is int cq)
            {
                var r = await RunEncodeAsync(cq, outputPath).ConfigureAwait(false);
                if (r.Ok && r.SizeBytes is not null)
                {
                    var size = r.SizeBytes.Value;
                    if (size >= minBytes && size <= maxBytes)
                    {
                        reporter.OnCompleted(new CompletedInfo(OutputRelativePath: outputRelative, OutputSuggestedExt: targetExt));
                        return;
                    }

                    trialBest = (Quality: cq, Size: size, Dist: ComputeDist(size, minBytes, maxBytes));
                }
                else
                {
                    lastError = r.ErrorMessage;
                }
            }

            // Binary search across quality.
            int lowQ = 5;
            int highQ = 95;

            for (var iter = 0; iter < 10 && lowQ <= highQ; iter++)
            {
                var midQ = (lowQ + highQ) / 2;
                var r = await RunEncodeAsync(midQ, outputPath).ConfigureAwait(false);
                if (!r.Ok || r.SizeBytes is null)
                {
                    lastError = r.ErrorMessage;
                    break;
                }

                var size = r.SizeBytes.Value;
                var dist = ComputeDist(size, minBytes, maxBytes);
                if (dist < trialBest.Dist)
                {
                    trialBest = (Quality: midQ, Size: size, Dist: dist);
                }

                if (size >= minBytes && size <= maxBytes)
                {
                    if (retainTargetSizeCompressionSettings && trialBest.Quality is int qOk)
                    {
                        _qualityCache[cacheKey] = qOk;
                    }

                    reporter.OnCompleted(new CompletedInfo(OutputRelativePath: outputRelative, OutputSuggestedExt: targetExt));
                    return;
                }

                // size monotonic: lower quality => smaller file
                if (size > maxBytes)
                {
                    highQ = midQ - 1;
                }
                else if (size < minBytes)
                {
                    lowQ = midQ + 1;
                }
            }

            // Not found in range: keep best.
            if (retainTargetSizeCompressionSettings && trialBest.Quality is int qBest)
            {
                _qualityCache[cacheKey] = qBest;
            }

            if (!File.Exists(outputPath))
            {
                reporter.OnFailed(new FailedInfo(lastError ?? "Failed to generate compressed output.", "IMAGEMAGICK_FAILED"));
                return;
            }

            reporter.OnCompleted(new CompletedInfo(OutputRelativePath: outputRelative, OutputSuggestedExt: targetExt));
            return;
        }

        // Fallback: single conversion (no target-size compression / non-lossy format / search skipped).
        {
            var r = await RunEncodeAsync(quality: null, outputPath).ConfigureAwait(false);
            if (!r.Ok)
            {
                reporter.OnFailed(new FailedInfo(r.ErrorMessage ?? "ImageMagick conversion failed.", "IMAGEMAGICK_FAILED"));
                return;
            }
        }

        reporter.OnCompleted(new CompletedInfo(
            OutputRelativePath: outputRelative,
            OutputSuggestedExt: targetExt
        ));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _qualityCache =
        new(System.StringComparer.OrdinalIgnoreCase);

    private static long ComputeDist(long size, long minBytes, long maxBytes)
    {
        if (size >= minBytes && size <= maxBytes) return 0;
        if (size < minBytes) return minBytes - size;
        return size - maxBytes;
    }

    private static async Task<string> EnsureMagickAsync(IProgressReporter reporter, CancellationToken ct)
    {
        // 1) Try local PATH first (fast path).
        var fromPath = TryGetMagickFromPath();
        if (fromPath is not null)
        {
            reporter.OnLog($"[imagemagick] found in PATH: {fromPath}");
            return fromPath;
        }

        // 2) Try cached install.
        var cached = Path.Combine(ImInstallDir, MagickExeName);
        if (File.Exists(cached))
        {
            reporter.OnLog($"[imagemagick] using cached: {cached}");
            return cached;
        }

        // 3) Download + extract portable archive to cache dir.
        reporter.OnLog("[imagemagick] magick not found, downloading portable package...");
        Directory.CreateDirectory(ImInstallDir);

        await DownloadAnyAsync(ImDownloadUrls, ImArchivePath, reporter, ct).ConfigureAwait(false);
        reporter.OnLog("[imagemagick] extracting portable package...");
        await ExtractSevenZipAsync(ImArchivePath, ImInstallDir, reporter, ct).ConfigureAwait(false);

        var magick = Directory.EnumerateFiles(ImInstallDir, MagickExeName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (magick is null)
        {
            throw new FileNotFoundException($"ImageMagick download/extract completed but {MagickExeName} not found in cache.");
        }

        reporter.OnLog($"[imagemagick] ready: {magick}");
        return magick;
    }

    private static string? TryGetMagickFromPath()
    {
        // Use PATH env (Windows ';' separator).
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var parts = pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            try
            {
                var candidate = Path.Combine(part.Trim(), MagickExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore invalid PATH entries
            }
        }
        return null;
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgressReporter reporter,
        CancellationToken ct)
    {
        // Reuse existing archive if present.
        if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 1024 * 1024)
            return;

        reporter.OnLog($"[imagemagick] downloading: {url}");

        using var http = new HttpClient();
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
    }

    private static async Task DownloadAnyAsync(
        IReadOnlyList<string> urls,
        string destinationPath,
        IProgressReporter reporter,
        CancellationToken ct)
    {
        // If there is already a sufficiently-sized cached archive, reuse it.
        if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 1024 * 1024)
            return;

        Exception? lastError = null;
        for (var i = 0; i < urls.Count; i++)
        {
            var url = urls[i];
            var tmpPath = destinationPath + $".part{i}";

            try
            {
                // Make sure stale partial file doesn't affect the next attempt.
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);

                reporter.OnLog($"[imagemagick] download try {i + 1}/{urls.Count}: {url}");
                await DownloadFileAsync(url, tmpPath, reporter, ct).ConfigureAwait(false);

                if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length <= 1024 * 1024)
                    throw new InvalidOperationException("Download completed but archive is missing/too small.");

                // Promote temp file to final cache path.
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                File.Move(tmpPath, destinationPath);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException)
            {
                // Keep trying other URLs. Cancellation should still stop the loop.
                lastError = ex;
                if (ct.IsCancellationRequested)
                    throw;

                reporter.OnLog($"[imagemagick] download failed: {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (ct.IsCancellationRequested)
                    throw;
                reporter.OnLog($"[imagemagick] download failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"All ImageMagick portable download candidates failed. Last error: {lastError?.GetType().Name}: {lastError?.Message}");
    }

    private static async Task ExtractSevenZipAsync(
        string archivePath,
        string destinationDir,
        IProgressReporter reporter,
        CancellationToken ct)
    {
        // .7z extraction: prefer local 7z, otherwise auto-download official 7zr.exe.
        var sevenZip = TryGet7ZipFromPath() ?? await Ensure7ZipAsync(reporter, ct).ConfigureAwait(false);

        Directory.CreateDirectory(destinationDir);

        reporter.OnLog("[imagemagick] extracting via 7z...");

        using var p = new Process();
        p.StartInfo = new ProcessStartInfo
        {
            FileName = sevenZip,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = destinationDir
        };

        // 7z: x <archive> -o<dir> -y
        p.StartInfo.ArgumentList.Add("x");
        p.StartInfo.ArgumentList.Add(archivePath);
        p.StartInfo.ArgumentList.Add($"-o{destinationDir}");
        p.StartInfo.ArgumentList.Add("-y");

        ct.Register(() =>
        {
            try
            {
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch
            {
                // best effort
            }
        });

        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) reporter.OnLog("[imagemagick] " + e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) reporter.OnLog("[imagemagick] " + e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();

        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"7z extraction failed with exit code {p.ExitCode}.");
        }
    }

    private static string? TryGet7ZipFromPath()
    {
        var parts = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            try
            {
                var c1 = Path.Combine(part.Trim(), "7z.exe");
                if (File.Exists(c1)) return c1;
                var c2 = Path.Combine(part.Trim(), "7za.exe");
                if (File.Exists(c2)) return c2;
            }
            catch
            {
                // ignore
            }
        }
        return null;
    }

    private static async Task<string> Ensure7ZipAsync(IProgressReporter reporter, CancellationToken ct)
    {
        Directory.CreateDirectory(SevenZipInstallDir);
        if (File.Exists(SevenZipExePath))
        {
            reporter.OnLog($"[imagemagick] using cached 7z: {SevenZipExePath}");
            return SevenZipExePath;
        }

        reporter.OnLog("[imagemagick] downloading 7z extractor (official 7-zip.org)...");
        await DownloadFileAsync(SevenZipDownloadUrl, SevenZipExePath, reporter, ct).ConfigureAwait(false);

        if (!File.Exists(SevenZipExePath))
            throw new FileNotFoundException("7zr.exe download failed; file not found at cache path.", SevenZipExePath);

        reporter.OnLog($"[imagemagick] 7z ready: {SevenZipExePath}");
        return SevenZipExePath;
    }
}

