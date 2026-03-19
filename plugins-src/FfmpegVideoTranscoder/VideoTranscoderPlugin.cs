using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PluginAbstractions;

namespace FfmpegVideoTranscoder;

public sealed class VideoTranscoderPlugin : IConverterPlugin
{
    private static readonly SemaphoreSlim FfmpegEnsureGate = new(1, 1);

    public PluginManifest GetManifest()
        => new PluginManifest(
            "ffmpeg.video.transcoder",
            "0.1.0",
            new[] { "mp4","mkv","mov","avi","webm","flv","wmv","m4v","ts","mts","m2ts","3gp","ogv" },
            new[]
            {
                new TargetFormat("mp4", "plugin/ffmpeg.video.transcoder/target/mp4", ""),
                new TargetFormat("mkv", "plugin/ffmpeg.video.transcoder/target/mkv", ""),
                new TargetFormat("mov", "plugin/ffmpeg.video.transcoder/target/mov", ""),
                new TargetFormat("webm", "plugin/ffmpeg.video.transcoder/target/webm", ""),
                new TargetFormat("avi", "plugin/ffmpeg.video.transcoder/target/avi", ""),
            },
            new ConfigSchema(Array.Empty<ConfigSection>()),
            new[] { "en-US", "zh-CN" },
            new I18nDescriptor("locales")
        );

    public async Task ExecuteAsync(ExecuteContext context, IProgressReporter reporter, CancellationToken cancellationToken = default)
    {
        reporter.OnProgress(new ProgressInfo(ProgressStage.Preparing, 0));

        var pluginDir = GetPluginDir();
        Directory.CreateDirectory(context.TempJobDir);

        var paths = await EnsureFfmpegAsync(pluginDir, reporter, cancellationToken);
        var ffmpeg = paths.FfmpegPath;
        var ffprobe = paths.FfprobePath;

        var crf = ReadIntFromSelectedConfig(context.SelectedConfig, "crf", fallback: 23);
        crf = Math.Clamp(crf, 0, 51);
        var hw = ReadStringFromSelectedConfig(context.SelectedConfig, "hw", fallback: "cpu");

        var inputPath = context.InputPath;
        if (!File.Exists(inputPath))
        {
            reporter.OnFailed(new FailedInfo($"Input file not found: {inputPath}", "INPUT_NOT_FOUND"));
            return;
        }

        var targetExt = (context.TargetFormatId ?? "mp4").Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(targetExt))
        {
            targetExt = "mp4";
        }

        var outputRelative = $"output.{targetExt}";
        var outputPath = Path.Combine(context.TempJobDir, outputRelative);

        reporter.OnLog($"[ffmpeg] input={inputPath}");
        reporter.OnLog($"[ffmpeg] output={outputPath}");
        reporter.OnLog($"[ffmpeg] hw={hw}, crf={crf}");

        var durationSeconds = await TryGetDurationSecondsAsync(ffprobe, inputPath, reporter, cancellationToken);

        var args = BuildFfmpegArgs(inputPath, outputPath, targetExt, hw, crf);
        reporter.OnLog("[ffmpeg] args: " + string.Join(" ", args.Select(QuoteIfNeeded)));

        reporter.OnProgress(new ProgressInfo(ProgressStage.Preparing, 100));
        reporter.OnProgress(new ProgressInfo(ProgressStage.Running, 0));

        var (exitCode, stderrTail) = await RunFfmpegWithProgressAsync(
            ffmpeg,
            args,
            durationSeconds,
            reporter,
            cancellationToken
        );

        if (exitCode != 0)
        {
            reporter.OnFailed(new FailedInfo($"ffmpeg failed (exit={exitCode}). {stderrTail}", "FFMPEG_FAILED"));
            return;
        }

        if (!File.Exists(outputPath))
        {
            reporter.OnFailed(new FailedInfo("ffmpeg completed but output file not found.", "OUTPUT_MISSING"));
            return;
        }

        reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
        reporter.OnCompleted(new CompletedInfo(outputRelative, OutputSuggestedExt: targetExt));
    }

    private static string GetPluginDir()
    {
        var loc = typeof(VideoTranscoderPlugin).Assembly.Location;
        return Path.GetDirectoryName(loc) ?? AppContext.BaseDirectory;
    }

    private static int ReadIntFromSelectedConfig(IReadOnlyDictionary<string, object?> selectedConfig, string key, int fallback)
    {
        if (!selectedConfig.TryGetValue(key, out var v) || v is null)
        {
            return fallback;
        }

        // Host Range returns string (ValueText), but be defensive.
        if (v is int i) return i;
        if (v is long l) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
        if (v is double d) return (int)Math.Round(d);
        if (v is float f) return (int)Math.Round(f);

        var s = v.ToString();
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static string ReadStringFromSelectedConfig(IReadOnlyDictionary<string, object?> selectedConfig, string key, string fallback)
    {
        if (!selectedConfig.TryGetValue(key, out var v) || v is null)
        {
            return fallback;
        }

        var s = (v.ToString() ?? "").Trim();
        return string.IsNullOrWhiteSpace(s) ? fallback : s;
    }

    private static IReadOnlyList<string> BuildFfmpegArgs(string inputPath, string outputPath, string targetExt, string hw, int crf)
    {
        // Always overwrite inside temp dir; Host will move/rename to final.
        var args = new List<string>
        {
            "-hide_banner",
            "-y",
            "-i", inputPath,
            "-progress", "pipe:1",
            "-nostats"
        };

        // Video encoder selection (simple mapping).
        hw = (hw ?? "cpu").Trim().ToLowerInvariant();
        switch (hw)
        {
            case "nvidia":
                // NVENC quality is not CRF; map to cq (0 best) but users expect "lower is better".
                // We'll invert into a usable range: cq ~ crf.
                args.AddRange(new[] { "-c:v", "h264_nvenc", "-cq", crf.ToString(CultureInfo.InvariantCulture) });
                break;
            case "amd":
                // AMF uses QP; map CRF directly.
                args.AddRange(new[]
                {
                    "-c:v", "h264_amf",
                    "-qp_i", crf.ToString(CultureInfo.InvariantCulture),
                    "-qp_p", crf.ToString(CultureInfo.InvariantCulture),
                    "-qp_b", crf.ToString(CultureInfo.InvariantCulture),
                });
                break;
            case "intel":
                // QSV uses global_quality (lower is better quality for some codecs; mapping is best-effort).
                args.AddRange(new[] { "-c:v", "h264_qsv", "-global_quality", crf.ToString(CultureInfo.InvariantCulture) });
                break;
            case "cpu":
            default:
                args.AddRange(new[] { "-c:v", "libx264", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-preset", "medium" });
                break;
        }

        // Audio: re-encode to AAC for broad container compatibility.
        // (Copying can fail when container doesn't support source codec.)
        args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });

        // Container tweaks for mp4.
        if (string.Equals(targetExt, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[] { "-movflags", "+faststart" });
        }

        args.Add(outputPath);
        return args;
    }

    private static async Task<(int exitCode, string stderrTail)> RunFfmpegWithProgressAsync(
        string ffmpegPath,
        IReadOnlyList<string> args,
        double? durationSeconds,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,  // progress
            RedirectStandardError = true,   // logs
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
        {
            p.StartInfo.ArgumentList.Add(a);
        }

        var stderrRing = new Queue<string>(capacity: 60);
        var stderrLock = new object();

        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderrLock)
            {
                if (stderrRing.Count >= 60) stderrRing.Dequeue();
                stderrRing.Enqueue(e.Data);
            }

            // Stream ffmpeg stderr into host UI log in real time.
            // (ffmpeg typically writes progress/details to stderr.)
            reporter.OnLog("[ffmpeg] " + e.Data);
        };

        var lastReported = -1;
        async Task PumpProgressAsync()
        {
            while (!p.HasExited && !cancellationToken.IsCancellationRequested)
            {
                var line = await p.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;

                // progress format: key=value
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var key = line[..idx];
                var value = line[(idx + 1)..];

                if (string.Equals(key, "out_time_ms", StringComparison.OrdinalIgnoreCase)
                    && durationSeconds is > 0
                    && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outTimeMs))
                {
                    var sec = outTimeMs / 1_000_000.0;
                    var percent = (int)Math.Round(Math.Clamp((sec / durationSeconds.Value) * 100.0, 0, 100));
                    if (percent != lastReported && (percent == 100 || percent - lastReported >= 1))
                    {
                        lastReported = percent;
                        reporter.OnProgress(new ProgressInfo(ProgressStage.Running, percent));
                    }
                }
                else if (string.Equals(key, "progress", StringComparison.OrdinalIgnoreCase)
                         && string.Equals(value, "end", StringComparison.OrdinalIgnoreCase))
                {
                    reporter.OnProgress(new ProgressInfo(ProgressStage.Running, 100));
                }
            }
        }

        p.Start();
        p.BeginErrorReadLine();

        var pumpTask = Task.Run(PumpProgressAsync, cancellationToken);

        try
        {
            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(p);
            throw;
        }
        finally
        {
            try { await pumpTask.ConfigureAwait(false); } catch { /* best effort */ }
        }

        string tail;
        lock (stderrLock)
        {
            tail = string.Join(" | ", stderrRing.TakeLast(12));
        }

        return (p.ExitCode, tail);
    }

    private static void TryKillProcessTree(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static async Task<double?> TryGetDurationSecondsAsync(string ffprobePath, string inputPath, IProgressReporter reporter, CancellationToken ct)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            p.StartInfo.ArgumentList.Add("-v");
            p.StartInfo.ArgumentList.Add("error");
            p.StartInfo.ArgumentList.Add("-show_entries");
            p.StartInfo.ArgumentList.Add("format=duration");
            p.StartInfo.ArgumentList.Add("-of");
            p.StartInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
            p.StartInfo.ArgumentList.Add(inputPath);

            p.Start();
            var output = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode != 0)
            {
                return null;
            }

            var s = (output ?? "").Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                return seconds;
            }

            return null;
        }
        catch (Exception ex)
        {
            reporter.OnLog($"[ffprobe] duration failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private sealed record FfmpegPaths(string FfmpegPath, string FfprobePath);

    private static async Task<FfmpegPaths> EnsureFfmpegAsync(string pluginDir, IProgressReporter reporter, CancellationToken ct)
    {
        // 1) Environment: explicit env var
        var env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var basePath = env.Trim().Trim('"');
            var ffPath = ResolveExePath(basePath, "ffmpeg");
            var fpPath = ResolveExePath(basePath, "ffprobe");
            if (ffPath is not null && fpPath is not null)
            {
                reporter.OnLog($"[ffmpeg] using FFMPEG_PATH: {basePath}");
                return new FfmpegPaths(ffPath, fpPath);
            }
        }

        // 2) PATH
        var ffmpegFromPath = FindOnPath("ffmpeg");
        var ffprobeFromPath = FindOnPath("ffprobe");
        if (ffmpegFromPath is not null && ffprobeFromPath is not null)
        {
            reporter.OnLog("[ffmpeg] using system PATH");
            return new FfmpegPaths(ffmpegFromPath, ffprobeFromPath);
        }

        // 3) plugin-local locations
        var candidates = new[]
        {
            pluginDir,
            Path.Combine(pluginDir, "tools", "ffmpeg"),
            Path.Combine(pluginDir, "tools", "ffmpeg", "bin"),
        };
        foreach (var c in candidates)
        {
            var ffPath = ResolveExePath(c, "ffmpeg");
            var fpPath = ResolveExePath(c, "ffprobe");
            if (ffPath is not null && fpPath is not null)
            {
                reporter.OnLog($"[ffmpeg] using plugin-local: {c}");
                return new FfmpegPaths(ffPath, fpPath);
            }
        }

        // 4) download (Windows only)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new InvalidOperationException("ffmpeg not found (PATH/plugin dir). Auto-download is implemented for Windows only.");
        }

        await FfmpegEnsureGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after waiting.
            foreach (var c in candidates)
            {
                var ffPath = ResolveExePath(c, "ffmpeg");
                var fpPath = ResolveExePath(c, "ffprobe");
                if (ffPath is not null && fpPath is not null)
                {
                    return new FfmpegPaths(ffPath, fpPath);
                }
            }

            var toolsDir = Path.Combine(pluginDir, "tools", "ffmpeg");
            Directory.CreateDirectory(toolsDir);

            reporter.OnLog("[ffmpeg] downloading FFmpeg (Windows) ...");

            // BtbN automated build (stable "latest" URL).
            var url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip";
            var zipPath = Path.Combine(toolsDir, "ffmpeg.zip");
            var extractDir = Path.Combine(toolsDir, "_extract");

            if (Directory.Exists(extractDir))
            {
                try { Directory.Delete(extractDir, recursive: true); } catch { /* best effort */ }
            }
            Directory.CreateDirectory(extractDir);

            using (var http = new HttpClient())
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(zipPath);
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            // The zip contains a top-level folder with bin/ffmpeg.exe.
            var ffmpegExe = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            var ffprobeExe = Directory.GetFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpegExe is null || ffprobeExe is null)
            {
                throw new InvalidOperationException("Downloaded FFmpeg zip did not contain ffmpeg.exe/ffprobe.exe.");
            }

            var binDir = Path.GetDirectoryName(ffmpegExe) ?? extractDir;
            var targetBin = Path.Combine(toolsDir, "bin");
            Directory.CreateDirectory(targetBin);

            // Copy bin folder to toolsDir/bin (include required dlls).
            foreach (var file in Directory.GetFiles(binDir))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(targetBin, name), overwrite: true);
            }

            // Clean up (keep zip for debugging? remove to save space).
            try { File.Delete(zipPath); } catch { /* best effort */ }
            try { Directory.Delete(extractDir, recursive: true); } catch { /* best effort */ }

            var ff = ResolveExePath(targetBin, "ffmpeg");
            var fp = ResolveExePath(targetBin, "ffprobe");
            if (ff is null || fp is null)
            {
                throw new InvalidOperationException("FFmpeg downloaded but executables not found in tools/ffmpeg/bin.");
            }

            reporter.OnLog("[ffmpeg] download OK: tools/ffmpeg/bin");
            return new FfmpegPaths(ff, fp);
        }
        finally
        {
            FfmpegEnsureGate.Release();
        }
    }

    private static string? FindOnPath(string exeNameNoExt)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var parts = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var full = ResolveExePath(p, exeNameNoExt);
            if (full is not null)
            {
                return full;
            }
        }
        return null;
    }

    private static string? ResolveExePath(string baseDirOrFile, string exeNameNoExt)
    {
        // base can be directory or direct exe path
        if (File.Exists(baseDirOrFile))
        {
            return baseDirOrFile;
        }

        var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        var exe = exeNameNoExt + ext;
        var candidate = Path.Combine(baseDirOrFile, exe);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string QuoteIfNeeded(string s)
        => s.Any(char.IsWhiteSpace) ? $"\"{s}\"" : s;
}

