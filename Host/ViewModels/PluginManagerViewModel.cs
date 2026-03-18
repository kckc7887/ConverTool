using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Host.ViewModels;

public sealed class PluginManagerViewModel : ObservableObject
{
    private readonly I18nService _i18n;

    public PluginManagerViewModel(I18nService i18n)
    {
        _i18n = i18n;

        Title = _i18n.T("host/pluginManager/title");
        RefreshLabel = _i18n.T("host/pluginManager/refresh");
        AddLabel = _i18n.T("host/pluginManager/add");
        DeleteLabel = _i18n.T("host/pluginManager/delete");
        ListHeader = _i18n.T("host/pluginManager/listHeader");
        Hint = _i18n.T("host/pluginManager/hint");

        RefreshCommand = new AsyncCommand(RefreshAsync);
        DeleteSelectedCommand = new AsyncCommand(DeleteSelectedAsync, () => Selected is not null);

        _i18n.LocaleChanged += (_, _) => ReloadStrings();
        _ = RefreshAsync();
    }

    public string Title { get; private set; } = "";
    public string RefreshLabel { get; private set; } = "";
    public string AddLabel { get; private set; } = "";
    public string DeleteLabel { get; private set; } = "";
    public string ListHeader { get; private set; } = "";
    public string Hint { get; private set; } = "";

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                RaisePropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ObservableCollection<PluginItemVm> Plugins { get; } = new();

    private PluginItemVm? _selected;
    public PluginItemVm? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RaisePropertyChanged(nameof(CanDelete));
                DeleteSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanDelete => Selected is not null;

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand DeleteSelectedCommand { get; }

    private void ReloadStrings()
    {
        Title = _i18n.T("host/pluginManager/title");
        RefreshLabel = _i18n.T("host/pluginManager/refresh");
        AddLabel = _i18n.T("host/pluginManager/add");
        DeleteLabel = _i18n.T("host/pluginManager/delete");
        ListHeader = _i18n.T("host/pluginManager/listHeader");
        Hint = _i18n.T("host/pluginManager/hint");

        RaisePropertyChanged(nameof(Title));
        RaisePropertyChanged(nameof(RefreshLabel));
        RaisePropertyChanged(nameof(AddLabel));
        RaisePropertyChanged(nameof(DeleteLabel));
        RaisePropertyChanged(nameof(ListHeader));
        RaisePropertyChanged(nameof(Hint));
    }

    private Task RefreshAsync()
    {
        StatusMessage = "";
        Plugins.Clear();
        Selected = null;

        var root = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(root))
        {
            return Task.CompletedTask;
        }

        foreach (var manifestPath in Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifestModel>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.PluginId))
                {
                    continue;
                }

                var pluginDir = Path.GetDirectoryName(manifestPath) ?? root;
                var exts = manifest.SupportedInputExtensions?.Length > 0
                    ? string.Join(", ", manifest.SupportedInputExtensions)
                    : "(none)";
                var formats = manifest.SupportedTargetFormats?.Length > 0
                    ? string.Join(", ", manifest.SupportedTargetFormats.Select(f => f.Id))
                    : "(none)";

                Plugins.Add(new PluginItemVm(
                    PluginId: manifest.PluginId,
                    Version: manifest.Version,
                    PluginDir: pluginDir,
                    Summary: $"{exts} → {formats}"
                ));
            }
            catch
            {
                // ignore malformed
            }
        }

        foreach (var p in Plugins.OrderBy(p => p.PluginId, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            // Re-order into sorted collection
        }

        var sorted = Plugins.OrderBy(p => p.PluginId, StringComparer.OrdinalIgnoreCase).ToList();
        Plugins.Clear();
        foreach (var p in sorted)
        {
            Plugins.Add(p);
        }

        return Task.CompletedTask;
    }

    private Task DeleteSelectedAsync()
    {
        if (Selected is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (Directory.Exists(Selected.PluginDir))
            {
                Directory.Delete(Selected.PluginDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.GetType().Name}: {ex.Message}";
        }

        return RefreshAsync();
    }

    public async Task InstallFromZipAsync(string zipPath)
    {
        StatusMessage = "";
        if (string.IsNullOrWhiteSpace(zipPath) || !zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "plugins");
        Directory.CreateDirectory(pluginsRoot);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ConverToolPluginInstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempRoot);

            var manifestPaths = Directory.GetFiles(tempRoot, "manifest.json", SearchOption.AllDirectories);
            if (manifestPaths.Length == 0)
            {
                StatusMessage = "Invalid plugin zip: manifest.json not found.";
                return;
            }

            if (manifestPaths.Length != 1)
            {
                StatusMessage = $"Invalid plugin zip: expected exactly 1 manifest.json, found {manifestPaths.Length}.";
                return;
            }

            var manifestPath = manifestPaths[0];
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifestModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.PluginId))
            {
                StatusMessage = "Invalid plugin manifest.json.";
                return;
            }

            if (!manifest.SupportsTerminationOnCancel)
            {
                StatusMessage = $"Skip plugin '{manifest.PluginId}': requires supportsTerminationOnCancel=true.";
                return;
            }

            var manifestDir = Path.GetDirectoryName(manifestPath) ?? tempRoot;
            var destDir = Path.Combine(pluginsRoot, manifest.PluginId);

            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, recursive: true);
            }

            Directory.CreateDirectory(destDir);
            CopyDirectoryRecursive(manifestDir, destDir);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Install failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }

        await RefreshAsync();
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
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

public sealed record PluginItemVm(string PluginId, string Version, string PluginDir, string Summary);

