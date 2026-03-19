using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
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

    public readonly record struct Result(bool Ok, string Message, string? PluginId);

    /// <summary>
    /// Extracts <paramref name="zipPath"/> to a temp folder, validates manifest, copies to <c>plugins/&lt;pluginId&gt;/</c>.
    /// </summary>
    public static async Task<Result> InstallFromZipAsync(string zipPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return new Result(false, "Please select a .zip file.", null);
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
                return new Result(false, "Invalid plugin zip: manifest.json not found.", null);
            }

            if (manifestPaths.Length != 1)
            {
                return new Result(false,
                    $"Invalid plugin zip: expected exactly 1 manifest.json, found {manifestPaths.Length}.", null);
            }

            var manifestPath = manifestPaths[0];
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifestModel>(json, ManifestJsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.PluginId))
            {
                return new Result(false, "Invalid plugin manifest.json.", null);
            }

            if (!manifest.SupportsTerminationOnCancel)
            {
                return new Result(false,
                    $"Skip plugin '{manifest.PluginId}': requires supportsTerminationOnCancel=true.", manifest.PluginId);
            }

            var manifestDir = Path.GetDirectoryName(manifestPath) ?? tempRoot;
            var destDir = Path.Combine(pluginsRoot, manifest.PluginId);

            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, recursive: true);
            }

            Directory.CreateDirectory(destDir);
            CopyDirectoryRecursive(manifestDir, destDir);

            return new Result(true, "", manifest.PluginId);
        }
        catch (Exception ex)
        {
            return new Result(false, $"Install failed: {ex.GetType().Name}: {ex.Message}", null);
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
}
