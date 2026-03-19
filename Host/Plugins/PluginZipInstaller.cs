using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Host.Plugins;

/// <summary>
/// Shared logic for installing a plugin from a .zip (used by main window and plugin manager).
/// </summary>
public static class PluginZipInstaller
{
    public static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static class ErrorCodes
    {
        public const string InvalidZip = "INVALID_ZIP";
        public const string ManifestNotFound = "MANIFEST_NOT_FOUND";
        public const string ManifestNotUnique = "MANIFEST_NOT_UNIQUE";
        public const string ManifestInvalid = "MANIFEST_INVALID";
        public const string MissingTerminationSupport = "MISSING_TERMINATION_SUPPORT";
        public const string FilesInUse = "FILES_IN_USE";
        public const string FilesLocked = "FILES_LOCKED";
        public const string Unknown = "UNKNOWN";
    }

    public readonly record struct Result(bool Ok, string ErrorCode, string? PluginId, string? Details);

    /// <summary>
    /// Extracts <paramref name="zipPath"/> to a temp folder, validates manifest, copies to <c>plugins/&lt;pluginId&gt;/</c>.
    /// </summary>
    public static async Task<Result> InstallFromZipAsync(string zipPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return new Result(false, ErrorCodes.InvalidZip, null, null);
        }

        var pluginsRoot = Path.Combine(baseDirectory, "plugins");
        Directory.CreateDirectory(pluginsRoot);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ConverToolPluginInstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempRoot);

            var manifestPaths = Directory.GetFiles(tempRoot, "manifest.json", SearchOption.AllDirectories);
            if (manifestPaths.Length == 0)
            {
                return new Result(false, ErrorCodes.ManifestNotFound, null, null);
            }

            if (manifestPaths.Length != 1)
            {
                return new Result(false, ErrorCodes.ManifestNotUnique, null, manifestPaths.Length.ToString());
            }

            var manifestPath = manifestPaths[0];
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifestModel>(json, ManifestJsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.PluginId))
            {
                return new Result(false, ErrorCodes.ManifestInvalid, null, null);
            }

            if (!manifest.SupportsTerminationOnCancel)
            {
                return new Result(false, ErrorCodes.MissingTerminationSupport, manifest.PluginId, null);
            }

            var manifestDir = Path.GetDirectoryName(manifestPath) ?? tempRoot;
            var destDir = Path.Combine(pluginsRoot, manifest.PluginId);

            if (Directory.Exists(destDir))
            {
                await DeleteDirectoryWithRetryAsync(destDir).ConfigureAwait(false);
            }

            Directory.CreateDirectory(destDir);
            CopyDirectoryRecursive(manifestDir, destDir);

            return new Result(true, "", manifest.PluginId, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new Result(false, ErrorCodes.FilesInUse, null, ex.Message);
        }
        catch (IOException ex)
        {
            return new Result(false, ErrorCodes.FilesLocked, null, ex.Message);
        }
        catch (Exception ex)
        {
            return new Result(false, ErrorCodes.Unknown, null, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    /// <summary>Copy directory contents (not the parent folder itself).</summary>
    public static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dirPath);
            var target = Path.Combine(destDir, rel);
            Directory.CreateDirectory(target);
        }

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, filePath);
            var target = Path.Combine(destDir, rel);
            File.Copy(filePath, target, overwrite: true);
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string dir)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    return;
                }

                // Plugin assemblies loaded via collectible ALC may need a GC cycle
                // before Windows releases file handles.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                await Task.Delay(120 * attempt).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(120 * attempt).ConfigureAwait(false);
            }
        }

        // Let caller produce user-facing error message.
        Directory.Delete(dir, recursive: true);
    }
}
