using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PluginAbstractions;

namespace FfmpegVideoTranscoder;

public sealed class VideoTranscoderPlugin : IConverterPlugin
{
    private static readonly SemaphoreSlim FfmpegEnsureGate = new(1, 1);
    private static string? _cachedHardwareEncoder;
    private static readonly object _hwCacheLock = new();

    private static readonly Dictionary<string, (int width, int height)> ResolutionPresets = new()
    {
        { "4k", (3840, 2160) },
        { "1080p", (1920, 1080) },
        { "720p", (1280, 720) },
        { "480p", (854, 480) }
    };

    public PluginManifest GetManifest()
        => new PluginManifest(
            "ffmpeg.video.transcoder",
            "1.1.0",
            new[] { "mp4","mkv","mov","avi","webm","flv","wmv","m4v","ts","mts","m2ts","3gp","ogv","ogg" },
            new[]
            {
                new TargetFormat("mp4", "plugin/ffmpeg.video.transcoder/target/mp4", ""),
                new TargetFormat("mkv", "plugin/ffmpeg.video.transcoder/target/mkv", ""),
                new TargetFormat("mov", "plugin/ffmpeg.video.transcoder/target/mov", ""),
                new TargetFormat("webm", "plugin/ffmpeg.video.transcoder/target/webm", ""),
                new TargetFormat("avi", "plugin/ffmpeg.video.transcoder/target/avi", ""),
                new TargetFormat("flv", "plugin/ffmpeg.video.transcoder/target/flv", ""),
                new TargetFormat("m4v", "plugin/ffmpeg.video.transcoder/target/m4v", ""),
                new TargetFormat("ts", "plugin/ffmpeg.video.transcoder/target/ts", ""),
                new TargetFormat("mts", "plugin/ffmpeg.video.transcoder/target/mts", ""),
                new TargetFormat("m2ts", "plugin/ffmpeg.video.transcoder/target/m2ts", ""),
                new TargetFormat("ogg", "plugin/ffmpeg.video.transcoder/target/ogg", ""),
                new TargetFormat("ogv", "plugin/ffmpeg.video.transcoder/target/ogv", ""),
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

        var enableAdvanced = ReadBoolFromSelectedConfig(context.SelectedConfig, "enableAdvancedConfig", fallback: false);
        var crf = enableAdvanced ? ReadIntFromSelectedConfig(context.SelectedConfig, "crf", fallback: 23) : 23;
        crf = Math.Clamp(crf, 0, 51);
        var hw = enableAdvanced ? ReadStringFromSelectedConfig(context.SelectedConfig, "hw", fallback: "auto") : "auto";
        var resolution = enableAdvanced ? ReadStringFromSelectedConfig(context.SelectedConfig, "resolution", fallback: "keep") : "keep";
        var framerate = enableAdvanced ? ReadStringFromSelectedConfig(context.SelectedConfig, "framerate", fallback: "keep") : "keep";

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
        reporter.OnLog($"[ffmpeg] hw={hw}, crf={crf}, resolution={resolution}, framerate={framerate}");

        var durationSeconds = await TryGetDurationSecondsAsync(ffprobe, inputPath, reporter, cancellationToken);

        var args = BuildFfmpegArgs(inputPath, outputPath, targetExt, hw, crf, resolution, framerate, ffmpeg, reporter);
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

    private static bool ReadBoolFromSelectedConfig(IReadOnlyDictionary<string, object?> selectedConfig, string key, bool fallback)
    {
        if (!selectedConfig.TryGetValue(key, out var v) || v is null)
        {
            return fallback;
        }

        if (v is bool b) return b;
        var s = v.ToString()?.Trim().ToLowerInvariant();
        return s == "true";
    }

    private static int ReadIntFromSelectedConfig(IReadOnlyDictionary<string, object?> selectedConfig, string key, int fallback)
    {
        if (!selectedConfig.TryGetValue(key, out var v) || v is null)
        {
            return fallback;
        }

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

    private static IReadOnlyList<string> BuildFfmpegArgs(
        string inputPath,
        string outputPath,
        string targetExt,
        string hw,
        int crf,
        string resolution,
        string framerate,
        string ffmpegPath,
        IProgressReporter reporter)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-y",
            "-i", inputPath,
            "-progress", "pipe:1",
            "-nostats"
        };

        var actualHw = hw;
        if (string.Equals(hw, "auto", StringComparison.OrdinalIgnoreCase))
        {
            actualHw = GetCachedHardwareEncoder(ffmpegPath, reporter);
            reporter.OnLog($"[ffmpeg] auto-detected hardware encoder: {actualHw}");
        }

        var videoFilters = new List<string>();

        if (!string.Equals(resolution, "keep", StringComparison.OrdinalIgnoreCase) &&
            ResolutionPresets.TryGetValue(resolution, out var res))
        {
            videoFilters.Add($"scale={res.width}:-2");
        }

        if (!string.Equals(framerate, "keep", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(framerate, out var fps) && fps > 0)
        {
            videoFilters.Add($"fps={fps}");
        }

        actualHw = (actualHw ?? "cpu").Trim().ToLowerInvariant();

        if (targetExt is "ogg" or "ogv")
        {
            args.AddRange(new[] { "-c:v", "libtheora", "-q:v", Math.Clamp(crf, 0, 10).ToString(CultureInfo.InvariantCulture) });
            args.AddRange(new[] { "-c:a", "libvorbis", "-q:a", "5" });
        }
        else
        {
            switch (actualHw)
            {
                case "nvidia":
                    args.AddRange(new[] { "-c:v", "h264_nvenc", "-cq", crf.ToString(CultureInfo.InvariantCulture) });
                    break;
                case "amd":
                    args.AddRange(new[]
                    {
                        "-c:v", "h264_amf",
                        "-qp_i", crf.ToString(CultureInfo.InvariantCulture),
                        "-qp_p", crf.ToString(CultureInfo.InvariantCulture),
                        "-qp_b", crf.ToString(CultureInfo.InvariantCulture),
                    });
                    break;
                case "intel":
                    args.AddRange(new[] { "-c:v", "h264_qsv", "-global_quality", crf.ToString(CultureInfo.InvariantCulture) });
                    break;
                case "cpu":
                default:
                    args.AddRange(new[] { "-c:v", "libx264", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-preset", "medium" });
                    break;
            }

            if (videoFilters.Count > 0)
            {
                args.AddRange(new[] { "-vf", string.Join(",", videoFilters) });
            }

            args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
        }

        if (string.Equals(targetExt, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[] { "-movflags", "+faststart" });
        }

        args.Add(outputPath);
        return args;
    }

    private static string GetCachedHardwareEncoder(string ffmpegPath, IProgressReporter reporter)
    {
        lock (_hwCacheLock)
        {
            if (!string.IsNullOrEmpty(_cachedHardwareEncoder))
            {
                return _cachedHardwareEncoder;
            }
        }

        var encoder = DetectHardwareEncoder(ffmpegPath, reporter);

        lock (_hwCacheLock)
        {
            _cachedHardwareEncoder = encoder;
        }

        return encoder;
    }

    private static string DetectHardwareEncoder(string ffmpegPath, IProgressReporter reporter)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-encoders -hide_banner",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            if (output.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase))
            {
                reporter.OnLog("[ffmpeg] detected: NVIDIA NVENC available");
                return "nvidia";
            }

            if (output.Contains("h264_qsv", StringComparison.OrdinalIgnoreCase))
            {
                reporter.OnLog("[ffmpeg] detected: Intel QSV available");
                return "intel";
            }

            if (output.Contains("h264_amf", StringComparison.OrdinalIgnoreCase))
            {
                reporter.OnLog("[ffmpeg] detected: AMD AMF available");
                return "amd";
            }

            reporter.OnLog("[ffmpeg] no hardware encoder detected, using CPU");
            return "cpu";
        }
        catch (Exception ex)
        {
            reporter.OnLog($"[ffmpeg] hardware detection failed: {ex.Message}, falling back to CPU");
            return "cpu";
        }
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
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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

            reporter.OnLog("[ffmpeg] " + e.Data);
        };

        var lastReported = -1;
        async Task PumpProgressAsync()
        {
            while (!p.HasExited && !cancellationToken.IsCancellationRequested)
            {
                var line = await p.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;

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
            try { await pumpTask.ConfigureAwait(false); } catch { }
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

    private static readonly string FfmpegVersion = "latest";
    private static readonly string FfmpegDownloadUrl =
        "https://ghfast.top/https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip";

    private static async Task<FfmpegPaths> EnsureFfmpegAsync(string pluginDir, IProgressReporter reporter, CancellationToken ct)
    {
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

        var ffmpegFromPath = FindOnPath("ffmpeg");
        var ffprobeFromPath = FindOnPath("ffprobe");
        if (ffmpegFromPath is not null && ffprobeFromPath is not null)
        {
            reporter.OnLog("[ffmpeg] using system PATH");
            return new FfmpegPaths(ffmpegFromPath, ffprobeFromPath);
        }

        var cachedDir = SharedToolCache.GetToolDir("ffmpeg", FfmpegVersion);
        var cachedFfmpeg = Path.Combine(cachedDir, "bin", "ffmpeg.exe");
        var cachedFfprobe = Path.Combine(cachedDir, "bin", "ffprobe.exe");

        if (File.Exists(cachedFfmpeg) && File.Exists(cachedFfprobe))
        {
            reporter.OnLog($"[ffmpeg] using cached: {cachedFfmpeg}");
            return new FfmpegPaths(cachedFfmpeg, cachedFfprobe);
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new InvalidOperationException("ffmpeg not found (PATH/plugin dir). Auto-download is implemented for Windows only.");
        }

        await FfmpegEnsureGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(cachedFfmpeg) && File.Exists(cachedFfprobe))
            {
                reporter.OnLog($"[ffmpeg] using cached: {cachedFfmpeg}");
                return new FfmpegPaths(cachedFfmpeg, cachedFfprobe);
            }

            reporter.OnLog("[ffmpeg] downloading FFmpeg...");

            await SharedToolCache.DownloadAndExtractAsync(
                "ffmpeg",
                FfmpegVersion,
                FfmpegDownloadUrl,
                cachedDir,
                reporter,
                ct);

            if (!File.Exists(cachedFfmpeg) || !File.Exists(cachedFfprobe))
            {
                var ffmpegExe = Directory.GetFiles(cachedDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                var ffprobeExe = Directory.GetFiles(cachedDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
                
                if (ffmpegExe is not null && ffprobeExe is not null)
                {
                    return new FfmpegPaths(ffmpegExe, ffprobeExe);
                }
                
                throw new InvalidOperationException("FFmpeg downloaded but executables not found.");
            }

            reporter.OnLog($"[ffmpeg] download OK: {cachedFfmpeg}");
            return new FfmpegPaths(cachedFfmpeg, cachedFfprobe);
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
