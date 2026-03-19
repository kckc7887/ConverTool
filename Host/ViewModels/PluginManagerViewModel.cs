using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Host.Plugins;

namespace Host.ViewModels;

public sealed class PluginManagerViewModel : ObservableObject
{
    private readonly I18nService _i18n;
    private readonly PluginI18nService _pluginI18n;

    public PluginManagerViewModel(I18nService i18n, PluginI18nService pluginI18n)
    {
        _i18n = i18n;
        _pluginI18n = pluginI18n;

        Title = _i18n.T("host/pluginManager/title");
        RefreshLabel = _i18n.T("host/pluginManager/refresh");
        AddLabel = _i18n.T("host/pluginManager/add");
        DeleteLabel = _i18n.T("host/pluginManager/delete");
        ListHeader = _i18n.T("host/pluginManager/listHeader");
        Hint = _i18n.T("host/pluginManager/hint");

        RefreshCommand = new AsyncCommand(RefreshAsync);
        DeleteSelectedCommand = new AsyncCommand(DeleteSelectedAsync, () => Selected is not null);

        _i18n.LocaleChanged += (_, _) =>
        {
            ReloadStrings();
            _ = RefreshAsync();
        };
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

    /// <summary>Used by <see cref="PluginManagerWindow"/> when StorageProvider / picker fails.</summary>
    public void SetStatusMessage(string message) => StatusMessage = message;

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
                var manifest = JsonSerializer.Deserialize<PluginManifestModel>(json, PluginZipInstaller.ManifestJsonOptions);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.PluginId))
                {
                    continue;
                }

                var pluginDir = Path.GetDirectoryName(manifestPath) ?? root;
                var entry = new PluginEntry(
                    PluginDir: pluginDir,
                    ManifestPath: manifestPath,
                    Manifest: manifest
                );

                var exts = manifest.SupportedInputExtensions?.Length > 0
                    ? string.Join(", ", manifest.SupportedInputExtensions)
                    : "(none)";
                var formats = manifest.SupportedTargetFormats?.Length > 0
                    ? string.Join(", ", manifest.SupportedTargetFormats.Select(f => f.Id))
                    : "(none)";

                var title = string.IsNullOrWhiteSpace(manifest.TitleKey)
                    ? manifest.PluginId
                    : _pluginI18n.T(entry, manifest.TitleKey, _i18n.Locale);

                var description = string.IsNullOrWhiteSpace(manifest.DescriptionKey)
                    ? ""
                    : _pluginI18n.T(entry, manifest.DescriptionKey, _i18n.Locale);

                Plugins.Add(new PluginItemVm(
                    PluginId: manifest.PluginId,
                    Version: manifest.Version,
                    PluginDir: pluginDir,
                    Title: title,
                    Description: description,
                    Summary: $"{exts} → {formats}"
                ));
            }
            catch
            {
                // ignore malformed
            }
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
            StatusMessage = _i18n.T("host/pluginManager/selectZip");
            await RefreshAsync();
            return;
        }

        var result = await PluginZipInstaller.InstallFromZipAsync(zipPath, AppContext.BaseDirectory);
        if (!result.Ok)
        {
            StatusMessage = result.Message;
        }

        await RefreshAsync();
    }
}

public sealed record PluginItemVm(
    string PluginId,
    string Version,
    string PluginDir,
    string Title,
    string Description,
    string Summary);

