using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Host.Plugins;
using Host.Settings;
using Host;
using PluginAbstractions;

namespace Host.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private static readonly HashSet<string> PersistableVmProps = new(StringComparer.Ordinal)
    {
        nameof(SelectedLanguage),
        nameof(OutputDir),
        nameof(UseInputDirAsOutput),
        nameof(NamingTemplate),
        nameof(EnableParallelProcessing),
        nameof(Parallelism),
        nameof(KeepTemp),
        nameof(SelectedTargetFormat),
    };

    private PluginCatalog _catalog;
    private readonly PluginI18nService _pluginI18n;
    private readonly I18nService _hostI18n;

    private PluginEntry? _activePlugin;

    private UserSettingsFile _userSettings = new();
    private bool _loadingUserSettings;
    private DispatcherTimer? _saveUserSettingsTimer;
    private readonly List<(ConfigFieldVm Field, PropertyChangedEventHandler Handler)> _configFieldHandlers = new();

    private bool _hasPlugins;
    public bool HasPlugins
    {
        get => _hasPlugins;
        private set
        {
            if (SetProperty(ref _hasPlugins, value))
            {
                RaisePropertyChanged(nameof(CanStart));
            }
        }
    }

    private bool _hasActivePlugin;
    public bool HasActivePlugin
    {
        get => _hasActivePlugin;
        private set
        {
            SetProperty(ref _hasActivePlugin, value);
        }
    }

    public string AddPluginLabel { get; private set; } = "";
    public string ManagePluginsLabel { get; private set; } = "";
    public string NoPluginHintLabel { get; private set; } = "";
    public bool CanStart => CanEdit && HasPlugins;

    public MainWindowViewModel(PluginCatalog catalog, PluginI18nService pluginI18n, I18nService hostI18n)
    {
        _catalog = catalog;
        HasPlugins = _catalog.Plugins.Count > 0;
        HasActivePlugin = false;
        _pluginI18n = pluginI18n;
        _hostI18n = hostI18n;

        _userSettings = UserSettingsStore.TryLoad() ?? new UserSettingsFile();
        if (!string.IsNullOrWhiteSpace(_userSettings.Locale))
        {
            _hostI18n.SetLocale(AppContext.BaseDirectory, _userSettings.Locale);
        }

        Languages = new ObservableCollection<string>(I18nService.SupportedLocales);
        _selectedLanguage = _hostI18n.Locale;
        RaisePropertyChanged(nameof(SelectedLanguage));

        HostTitle = _hostI18n.T("host/app/title");
        LanguageLabel = _hostI18n.T("host/app/language");
        InputHeader = _hostI18n.T("host/section/input");
        ConfigHeader = _hostI18n.T("host/section/config");
        ProcessHeader = _hostI18n.T("host/section/process");
        OutputHeader = _hostI18n.T("host/section/output");
        InputPlaceholder = _hostI18n.T("host/input/placeholder");
        InputBrowseLabel = _hostI18n.T("host/input/browse");
        OutputBrowseLabel = _hostI18n.T("host/output/browse");
        InputPathLabel = _hostI18n.T("host/input/pathLabel");
        ConfigPlaceholder = HasPlugins
            ? _hostI18n.T("host/config/activateByInput")
            : _hostI18n.T("host/config/noPluginHint");
        AddPluginLabel = _hostI18n.T("host/app/addPlugin");
        ManagePluginsLabel = _hostI18n.T("host/app/managePlugins");
        NoPluginHintLabel = _hostI18n.T("host/config/noPluginHint");
        ProcessPlaceholder = _hostI18n.T("host/process/placeholder");
        OutputPlaceholder = _hostI18n.T("host/output/placeholder");
        TargetFormatLabel = _hostI18n.T("host/config/targetFormat");
        OutputDirLabel = _hostI18n.T("host/output/dirLabel");
        NamingTemplateLabel = _hostI18n.T("host/output/templateLabel");
        NamingTemplateHelp = _hostI18n.T("host/output/templateHelp");
        UseInputDirLabel = _hostI18n.T("host/output/useInputDir");
        KeepTempLabel = _hostI18n.T("host/process/keepTemp");
        StartLabel = _hostI18n.T("host/process/start");
        PauseLabel = _hostI18n.T("host/process/pause");
        PauseTooltipLabel = _hostI18n.T("host/process/pauseTooltip");
        StopLabel = _hostI18n.T("host/process/stop");
        EnableParallelLabel = _hostI18n.T("host/process/parallelEnable");
        ParallelismLabel = _hostI18n.T("host/process/parallelism");
        ProcessLogSectionLabel = _hostI18n.T("host/process/logSection");

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        OutputDir = string.IsNullOrWhiteSpace(docs)
            ? Path.Combine(AppContext.BaseDirectory, "output")
            : Path.Combine(docs, "ConverToolOutput");
        NamingTemplate = "{base}.{ext}";

        _loadingUserSettings = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(_userSettings.OutputDir))
            {
                OutputDir = _userSettings.OutputDir.Trim();
            }

            UseInputDirAsOutput = _userSettings.UseInputDirAsOutput ?? true;
            NamingTemplate = string.IsNullOrWhiteSpace(_userSettings.NamingTemplate)
                ? "{base}.{ext}"
                : _userSettings.NamingTemplate.Trim();
            EnableParallelProcessing = _userSettings.EnableParallelProcessing;
            Parallelism = Math.Clamp(_userSettings.Parallelism, 1, 8);
            KeepTemp = _userSettings.KeepTemp;
            // Input paths are runtime data; keep it empty on next launch.
            InputPaths = "";
        }
        finally
        {
            _loadingUserSettings = false;
        }

        StartCommand = new AsyncCommand(StartAsync);
        BrowseInputCommand = new AsyncCommand(BrowseInputAsync);
        BrowseOutputDirCommand = new AsyncCommand(BrowseOutputDirAsync);
        BrowseConfigPathCommand = new AsyncCommand<PathFieldVm>(BrowseConfigPathAsync);
        PauseCommand = new AsyncCommand(TogglePauseAsync);
        StopCommand = new AsyncCommand(StopAsync);
        AddPluginCommand = new AsyncCommand(AddPluginAsync);

        _hostI18n.LocaleChanged += (_, _) => ReloadHostStrings();
        PropertyChanged += OnVmPersistablePropertyChanged;
        ReloadPluginContext();
    }

    // ---- host strings ----
    private string _hostTitle = "";
    public string HostTitle { get => _hostTitle; set => SetProperty(ref _hostTitle, value); }

    public ObservableCollection<string> Languages { get; }

    private string _selectedLanguage = "en-US";
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
            {
                _hostI18n.SetLocale(AppContext.BaseDirectory, value);
            }
        }
    }

    public string InputHeader { get; private set; } = "";
    public string ConfigHeader { get; private set; } = "";
    public string ProcessHeader { get; private set; } = "";
    public string OutputHeader { get; private set; } = "";
    public string InputPlaceholder { get; private set; } = "";
    public string InputBrowseLabel { get; private set; } = "";
    public string OutputBrowseLabel { get; private set; } = "";
    public string InputPathLabel { get; private set; } = "";
    public string ConfigPlaceholder { get; private set; } = "";
    public string ProcessPlaceholder { get; private set; } = "";
    public string OutputPlaceholder { get; private set; } = "";
    public string TargetFormatLabel { get; private set; } = "";
    public string OutputDirLabel { get; private set; } = "";
    public string NamingTemplateLabel { get; private set; } = "";
    public string NamingTemplateHelp { get; private set; } = "";
    public string UseInputDirLabel { get; private set; } = "";
    public string KeepTempLabel { get; private set; } = "";
    public string StartLabel { get; private set; } = "";
    public string PauseLabel { get; private set; } = "";
    public string PauseTooltipLabel { get; private set; } = "";
    public string StopLabel { get; private set; } = "";
    public string ProcessLogSectionLabel { get; private set; } = "";
    public string EnableParallelLabel { get; private set; } = "";
    public string ParallelismLabel { get; private set; } = "";
    public string LanguageLabel { get; private set; } = "";

    // ---- inputs ----
    private string _inputPaths = "";
    public string InputPaths
    {
        get => _inputPaths;
        set
        {
            if (!SetProperty(ref _inputPaths, value))
            {
                return;
            }

            if (!_loadingUserSettings)
            {
                ReloadPluginContext();
            }
        }
    }

    private string _outputDir = "";
    public string OutputDir { get => _outputDir; set => SetProperty(ref _outputDir, value); }

    private bool _useInputDirAsOutput = true;
    public bool UseInputDirAsOutput { get => _useInputDirAsOutput; set => SetProperty(ref _useInputDirAsOutput, value); }

    private bool _enableParallelProcessing;
    public bool EnableParallelProcessing { get => _enableParallelProcessing; set => SetProperty(ref _enableParallelProcessing, value); }

    private int _parallelism = 2;
    public int Parallelism { get => _parallelism; set => SetProperty(ref _parallelism, Math.Clamp(value, 1, 8)); }

    private string _namingTemplate = "";
    public string NamingTemplate { get => _namingTemplate; set => SetProperty(ref _namingTemplate, value); }

    private bool _keepTemp;
    public bool KeepTemp { get => _keepTemp; set => SetProperty(ref _keepTemp, value); }

    // ---- plugin-driven UI ----
    public ObservableCollection<TargetFormatVm> TargetFormats { get; } = new();

    private TargetFormatVm? _selectedTargetFormat;
    public TargetFormatVm? SelectedTargetFormat { get => _selectedTargetFormat; set => SetProperty(ref _selectedTargetFormat, value); }

    public ObservableCollection<ConfigFieldVm> ConfigFields { get; } = new();

    // ---- process/output ----
    private string _processLog = "";
    public string ProcessLog { get => _processLog; set => SetProperty(ref _processLog, value); }

    private int _progress;
    public int Progress { get => _progress; set => SetProperty(ref _progress, value); }

    private string _progressText = "";
    public string ProgressText { get => _progressText; set => SetProperty(ref _progressText, value); }

    private string _batchText = "";
    public string BatchText { get => _batchText; set => SetProperty(ref _batchText, value); }

    private string _outputText = "";
    public string OutputText { get => _outputText; set => SetProperty(ref _outputText, value); }

    public AsyncCommand StartCommand { get; }
    public AsyncCommand BrowseInputCommand { get; }
    public AsyncCommand BrowseOutputDirCommand { get; }
    public AsyncCommand<PathFieldVm> BrowseConfigPathCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand AddPluginCommand { get; }

    // Host will set this from code-behind (TopLevel required for picker).
    public TopLevel? TopLevel { get; set; }
    public Func<string, Task>? ShowErrorDialogAsync { get; set; }

    private CancellationTokenSource? _runCts;
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
    private bool _paused;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(CanEdit));
                RaisePropertyChanged(nameof(CanStart));
            }
        }
    }

    public bool CanEdit => !_isBusy;

    private void ReloadHostStrings()
    {
        HostTitle = _hostI18n.T("host/app/title");
        LanguageLabel = _hostI18n.T("host/app/language");

        InputHeader = _hostI18n.T("host/section/input");
        ConfigHeader = _hostI18n.T("host/section/config");
        ProcessHeader = _hostI18n.T("host/section/process");
        OutputHeader = _hostI18n.T("host/section/output");

        InputPlaceholder = _hostI18n.T("host/input/placeholder");
        InputBrowseLabel = _hostI18n.T("host/input/browse");
        OutputBrowseLabel = _hostI18n.T("host/output/browse");
        InputPathLabel = _hostI18n.T("host/input/pathLabel");
        ConfigPlaceholder = HasPlugins
            ? (HasAnyInputPaths()
                ? _hostI18n.T("host/config/placeholder")
                : _hostI18n.T("host/config/activateByInput"))
            : _hostI18n.T("host/config/noPluginHint");
        NoPluginHintLabel = _hostI18n.T("host/config/noPluginHint");
        AddPluginLabel = _hostI18n.T("host/app/addPlugin");
        ManagePluginsLabel = _hostI18n.T("host/app/managePlugins");
        ProcessPlaceholder = _hostI18n.T("host/process/placeholder");
        OutputPlaceholder = _hostI18n.T("host/output/placeholder");
        TargetFormatLabel = _hostI18n.T("host/config/targetFormat");
        OutputDirLabel = _hostI18n.T("host/output/dirLabel");
        NamingTemplateLabel = _hostI18n.T("host/output/templateLabel");
        NamingTemplateHelp = _hostI18n.T("host/output/templateHelp");
        UseInputDirLabel = _hostI18n.T("host/output/useInputDir");
        KeepTempLabel = _hostI18n.T("host/process/keepTemp");
        StartLabel = _hostI18n.T("host/process/start");
        PauseLabel = _hostI18n.T("host/process/pause");
        PauseTooltipLabel = _hostI18n.T("host/process/pauseTooltip");
        StopLabel = _hostI18n.T("host/process/stop");
        EnableParallelLabel = _hostI18n.T("host/process/parallelEnable");
        ParallelismLabel = _hostI18n.T("host/process/parallelism");
        ProcessLogSectionLabel = _hostI18n.T("host/process/logSection");

        RaisePropertyChanged(nameof(LanguageLabel));
        RaisePropertyChanged(nameof(InputHeader));
        RaisePropertyChanged(nameof(ConfigHeader));
        RaisePropertyChanged(nameof(ProcessHeader));
        RaisePropertyChanged(nameof(OutputHeader));
        RaisePropertyChanged(nameof(InputPlaceholder));
        RaisePropertyChanged(nameof(InputBrowseLabel));
        RaisePropertyChanged(nameof(OutputBrowseLabel));
        RaisePropertyChanged(nameof(InputPathLabel));
        RaisePropertyChanged(nameof(ConfigPlaceholder));
        RaisePropertyChanged(nameof(AddPluginLabel));
        RaisePropertyChanged(nameof(ManagePluginsLabel));
        RaisePropertyChanged(nameof(NoPluginHintLabel));
        RaisePropertyChanged(nameof(ProcessPlaceholder));
        RaisePropertyChanged(nameof(OutputPlaceholder));
        RaisePropertyChanged(nameof(TargetFormatLabel));
        RaisePropertyChanged(nameof(OutputDirLabel));
        RaisePropertyChanged(nameof(NamingTemplateLabel));
        RaisePropertyChanged(nameof(NamingTemplateHelp));
        RaisePropertyChanged(nameof(UseInputDirLabel));
        RaisePropertyChanged(nameof(KeepTempLabel));
        RaisePropertyChanged(nameof(StartLabel));
        RaisePropertyChanged(nameof(PauseLabel));
        RaisePropertyChanged(nameof(PauseTooltipLabel));
        RaisePropertyChanged(nameof(StopLabel));
        RaisePropertyChanged(nameof(ProcessLogSectionLabel));
        RaisePropertyChanged(nameof(EnableParallelLabel));
        RaisePropertyChanged(nameof(ParallelismLabel));

        // plugin strings depend on locale too
        ReloadPluginContext();
    }

    private void ReloadPluginContext()
    {
        DetachConfigFieldHandlers();

        _activePlugin = ResolveActivePlugin();
        var plugin = _activePlugin;
        HasActivePlugin = plugin?.Manifest is not null;

        TargetFormats.Clear();
        ConfigFields.Clear();

        if (!HasActivePlugin)
        {
            SelectedTargetFormat = null;
            return;
        }

        foreach (var tf in plugin!.Manifest.SupportedTargetFormats ?? Array.Empty<TargetFormatModel>())
        {
            TargetFormats.Add(new TargetFormatVm(tf.Id, _pluginI18n.T(plugin, tf.DisplayNameKey, _hostI18n.Locale)));
        }
        SelectedTargetFormat = TargetFormats.FirstOrDefault();

        var schema = plugin!.Manifest.ConfigSchema;
        if (schema?.Sections is not null)
        {
            foreach (var section in schema.Sections)
            {
                foreach (var field in section.Fields ?? Array.Empty<ConfigFieldModel>())
                {
                    if (string.IsNullOrWhiteSpace(field.Key))
                    {
                        continue;
                    }

                    var label = _pluginI18n.T(plugin, field.LabelKey, _hostI18n.Locale);
                    var help = string.IsNullOrWhiteSpace(field.HelpKey) ? null : _pluginI18n.T(plugin, field.HelpKey, _hostI18n.Locale);
                    var type = (field.Type ?? "Text").Trim();

                    ConfigFieldVm vm = type switch
                    {
                        "Checkbox" => new CheckboxFieldVm(field.Key, label, help, defaultValue: false),
                        "Select" => new SelectFieldVm(
                            field.Key,
                            label,
                            help,
                            (field.Options ?? Array.Empty<ConfigOptionModel>())
                                .Select(o => new OptionVm(o.Id, _pluginI18n.T(plugin!, o.LabelKey, _hostI18n.Locale)))
                                .ToList(),
                            defaultId: TryGetStringDefault(field.DefaultValue)
                        ),
                        "Path" => new PathFieldVm(field.Key, label, help, TryGetStringDefault(field.DefaultValue), field.Path?.Kind ?? "File"),
                        "Range" => new RangeFieldVm(
                            field.Key,
                            label,
                            help,
                            min: field.Range?.Min ?? 0,
                            max: field.Range?.Max ?? 100,
                            step: field.Range?.Step ?? 1,
                            defaultValue: TryGetDoubleDefault(field.DefaultValue, field.Range?.Min ?? 0)
                        ),
                        "Number" => new NumberFieldVm(
                            field.Key,
                            label,
                            help,
                            min: field.Range?.Min ?? 0,
                            max: field.Range?.Max ?? 100,
                            step: field.Range?.Step ?? 1,
                            defaultValue: TryGetDoubleDefault(field.DefaultValue, field.Range?.Min ?? 0)
                        ),
                        _ => new TextFieldVm(field.Key, label, help, TryGetStringDefault(field.DefaultValue))
                    };

                    ConfigFields.Add(vm);
                }
            }
        }

        _loadingUserSettings = true;
        try
        {
            RestorePluginUiFromSettings();
        }
        finally
        {
            _loadingUserSettings = false;
        }

        AttachConfigFieldHandlers();
    }

    private void OnVmPersistablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingUserSettings)
        {
            return;
        }

        if (e.PropertyName is null || !PersistableVmProps.Contains(e.PropertyName))
        {
            return;
        }

        ScheduleSaveUserSettings();
    }

    private void ScheduleSaveUserSettings()
    {
        if (_loadingUserSettings)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _saveUserSettingsTimer ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(500),
                DispatcherPriority.Background,
                (_, _) => FlushSaveUserSettings());
            _saveUserSettingsTimer.Stop();
            _saveUserSettingsTimer.Start();
        }, DispatcherPriority.Background);
    }

    public void SaveUserSettingsNow() => FlushSaveUserSettings();

    private void FlushSaveUserSettings()
    {
        try
        {
            _saveUserSettingsTimer?.Stop();
            UserSettingsStore.Save(CaptureCurrentUserSettings());
        }
        catch
        {
            // best effort
        }
    }

    private UserSettingsFile CaptureCurrentUserSettings()
    {
        _userSettings.Locale = _hostI18n.Locale;
        // Don't persist InputPaths (user's runtime selection).
        _userSettings.InputPaths = null;
        _userSettings.OutputDir = _outputDir;
        _userSettings.UseInputDirAsOutput = _useInputDirAsOutput;
        _userSettings.NamingTemplate = _namingTemplate;
        _userSettings.EnableParallelProcessing = _enableParallelProcessing;
        _userSettings.Parallelism = _parallelism;
        _userSettings.KeepTemp = _keepTemp;

        if (_activePlugin?.Manifest?.PluginId is { } pid)
        {
            if (!_userSettings.Plugins.TryGetValue(pid, out var plug))
            {
                plug = new PluginUserSettings();
                _userSettings.Plugins[pid] = plug;
            }

            plug.TargetFormatId = SelectedTargetFormat?.Id;
            plug.Fields.Clear();
            foreach (var f in ConfigFields)
            {
                var v = f.GetValue();
                plug.Fields[f.Key] = v switch
                {
                    null => "",
                    bool b => b ? "true" : "false",
                    _ => v.ToString() ?? ""
                };
            }
        }

        return _userSettings;
    }

    private void RestorePluginUiFromSettings()
    {
        if (_activePlugin?.Manifest?.PluginId is not { } pid)
        {
            return;
        }

        if (!_userSettings.Plugins.TryGetValue(pid, out var ps))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ps.TargetFormatId))
        {
            var tf = TargetFormats.FirstOrDefault(t =>
                string.Equals(t.Id, ps.TargetFormatId, StringComparison.OrdinalIgnoreCase));
            if (tf is not null)
            {
                SelectedTargetFormat = tf;
            }
        }

        foreach (var field in ConfigFields)
        {
            if (!ps.Fields.TryGetValue(field.Key, out var raw))
            {
                continue;
            }

            ApplyFieldString(field, raw);
        }
    }

    private static void ApplyFieldString(ConfigFieldVm field, string raw)
    {
        switch (field)
        {
            case TextFieldVm t:
                t.Text = raw;
                break;
            case PathFieldVm p:
                p.Path = raw;
                break;
            case CheckboxFieldVm c:
                if (bool.TryParse(raw, out var cb))
                {
                    c.IsChecked = cb;
                }
                else if (string.Equals(raw, "1", StringComparison.Ordinal))
                {
                    c.IsChecked = true;
                }
                else if (string.Equals(raw, "0", StringComparison.Ordinal))
                {
                    c.IsChecked = false;
                }

                break;
            case SelectFieldVm s:
                var opt = s.Options.FirstOrDefault(o =>
                    string.Equals(o.Id, raw, StringComparison.OrdinalIgnoreCase));
                if (opt is not null)
                {
                    s.Selected = opt;
                }

                break;
            case RangeFieldVm r:
                r.ValueText = raw;
                break;
            case NumberFieldVm n:
                // Stored as invariant-culture string.
                if (decimal.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv))
                {
                    n.Value = dv;
                }
                else
                {
                    // Best effort: keep current value if parse fails.
                }
                break;
        }
    }

    private void AttachConfigFieldHandlers()
    {
        DetachConfigFieldHandlers();
        foreach (var f in ConfigFields)
        {
            PropertyChangedEventHandler h = (_, _) =>
            {
                if (_loadingUserSettings)
                {
                    return;
                }

                ScheduleSaveUserSettings();
            };
            f.PropertyChanged += h;
            _configFieldHandlers.Add((f, h));
        }
    }

    private void DetachConfigFieldHandlers()
    {
        foreach (var (f, h) in _configFieldHandlers)
        {
            try
            {
                f.PropertyChanged -= h;
            }
            catch
            {
                // best effort
            }
        }

        _configFieldHandlers.Clear();
    }

    private PluginEntry? ResolveActivePlugin()
    {
        var firstInput = (InputPaths ?? "")
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        if (string.IsNullOrWhiteSpace(firstInput))
        {
            return null;
        }

        return PluginRouter.RouteByInputPath(_catalog, firstInput) ?? _catalog.Plugins.FirstOrDefault();
    }

    private bool HasAnyInputPaths()
    {
        return (InputPaths ?? "")
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Any(s => !string.IsNullOrWhiteSpace(s));
    }

    private async Task StartAsync()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        _paused = false;
        _pauseGate.Set();
        IsBusy = true;
        try
        {
            ProcessLog = "";
            OutputText = "";
            Progress = 0;
            ProgressText = "";
            BatchText = "";

            var inputPaths = (InputPaths ?? "")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            if (inputPaths.Length == 0)
            {
                AppendLog("[host] No input paths.");
                return;
            }

            if (EnableParallelProcessing)
            {
                await RunParallelAsync(inputPaths, ct);
            }
            else
            {
                await RunSerialAsync(inputPaths, ct);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunSerialAsync(string[] inputPaths, CancellationToken ct)
    {
        var results = new List<string>(inputPaths.Length);
        var completed = 0;
        var total = inputPaths.Length;
        UpdateBatch(completed, total, "Idle");

        for (var i = 0; i < inputPaths.Length; i++)
        {
            await WaitIfPausedAsync(ct);

            var input = inputPaths[i];
            var index = i + 1;

            var entry = PluginRouter.RouteByInputPath(_catalog, input);
            if (entry is null)
            {
                var msg = $"No plugin matched input: {input}";
                AppendLog("[host] " + msg);
                results.Add($"FAIL  {input}{Environment.NewLine}      {msg}");
                completed++;
                UpdateBatch(completed, total, "NoMatch");
                continue;
            }

            using var handle = PluginRuntimeLoader.TryLoadPlugin(entry);
            if (handle is null)
            {
                var msg = $"Failed to load plugin: {entry.Manifest.PluginId}";
                AppendLog("[host] " + msg);
                results.Add($"FAIL  {input}{Environment.NewLine}      {msg}");
                completed++;
                UpdateBatch(completed, total, "LoadFail");
                continue;
            }
            var plugin = handle.Instance;

            var jobId = Guid.NewGuid().ToString("N");
            var tempJobDir = Path.Combine(Path.GetTempPath(), "ConverTool", jobId, index.ToString());
            Directory.CreateDirectory(tempJobDir);

            var targetFormatId = SelectedTargetFormat?.Id
                                 ?? entry.Manifest.SupportedTargetFormats.FirstOrDefault()?.Id
                                 ?? "txt";

            var selectedConfig = ConfigFields.ToDictionary(f => f.Key, f => f.GetValue(), StringComparer.OrdinalIgnoreCase);

            string? finalPath = null;
            string? fail = null;

            var reporter = new VmReporter(
                onLog: AppendLog,
                onProgress: info =>
                {
                    var perFile = MapToOverallPercent(info.Stage, info.PercentWithinStage);
                    var overall = MapBatchToOverallPercent(completed, total, perFile);
                    Dispatcher.UIThread.Post(() =>
                    {
                        Progress = overall;
                        ProgressText = $"{overall}% · {info.Stage}";
                    });
                },
                onCompleted: info =>
                {
                    var tempOut = Path.Combine(tempJobDir, info.OutputRelativePath);
                    try
                    {
                        finalPath = MoveToFinalOutput(tempOut, input, targetFormatId, index);
                    }
                    catch (Exception ex)
                    {
                        fail = $"Move failed: {ex.GetType().Name}: {ex.Message}";
                        AppendLog("[host] " + fail);
                    }
                },
                onFailed: info => fail = info.ErrorMessage
            );

            AppendLog($"[host] Running ({index}/{total}) pluginId={entry.Manifest.PluginId}");

            try
            {
                await plugin.ExecuteAsync(
                    new ExecuteContext(
                        JobId: jobId,
                        InputPath: input,
                        TempJobDir: tempJobDir,
                        TargetFormatId: targetFormatId,
                        SelectedConfig: selectedConfig,
                        Locale: _hostI18n.Locale,
                        OutputNamingContext: new Dictionary<string, object?>
                        {
                            ["base"] = Path.GetFileNameWithoutExtension(input),
                            ["index"] = index,
                            ["ext"] = targetFormatId
                        }
                    ),
                    reporter,
                    ct
                );
            }
            catch (OperationCanceledException)
            {
                fail = "Canceled";
                AppendLog("[host] Canceled.");
            }
            catch (Exception ex)
            {
                fail = $"{ex.GetType().Name}: {ex.Message}";
                AppendLog("[host] Exception: " + fail);
            }
            finally
            {
                if (!KeepTemp)
                {
                    TryDeleteDirectory(tempJobDir);
                }
            }

            completed++;
            UpdateBatch(completed, total, finalPath is null ? "Fail" : "Ok");

            results.Add(finalPath is null
                ? $"FAIL  {input}{Environment.NewLine}      {fail ?? "Unknown error"}"
                : $"OK    {input}{Environment.NewLine}      {finalPath}");
        }

        Dispatcher.UIThread.Post(() =>
        {
            Progress = 100;
            ProgressText = "100% · Done";
            OutputText = string.Join(Environment.NewLine, results);
        });
    }

    private async Task RunParallelAsync(string[] inputPaths, CancellationToken ct)
    {
        var maxConcurrency = Math.Clamp(Parallelism, 1, 8);

        var total = inputPaths.Length;
        var completed = 0;
        var results = new string?[total];
        var perFilePercents = new int[total];
        var gate = new object();

        UpdateBatch(0, total, "Parallel");

        void UpdateOverallFromWorker(int fileIndex, ProgressStage stage, int perFileOverallPercent)
        {
            lock (gate)
            {
                perFilePercents[fileIndex] = Math.Clamp(perFileOverallPercent, 0, 100);
                var sum = 0;
                for (var i = 0; i < total; i++)
                {
                    sum += perFilePercents[i];
                }
                var overall = (int)Math.Round(sum / (double)total);
                Dispatcher.UIThread.Post(() =>
                {
                    Progress = overall;
                    ProgressText = $"{overall}% · Parallel";
                });
            }
        }

        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task>(total);

        for (var i = 0; i < total; i++)
        {
            var fileIndex = i;
            var input = inputPaths[i];
            var index = i + 1;

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await WaitIfPausedAsync(ct);
                    UpdateOverallFromWorker(fileIndex, ProgressStage.Preparing, 1);

                    var entry = PluginRouter.RouteByInputPath(_catalog, input);
                    if (entry is null)
                    {
                        var msg = $"No plugin matched input: {input}";
                        AppendLog("[host] " + msg);
                        results[fileIndex] = $"FAIL  {input}{Environment.NewLine}      {msg}";
                        UpdateOverallFromWorker(fileIndex, ProgressStage.Finalizing, 100);
                        return;
                    }

                    using var handle = PluginRuntimeLoader.TryLoadPlugin(entry);
                    if (handle is null)
                    {
                        var msg = $"Failed to load plugin: {entry.Manifest.PluginId}";
                        AppendLog("[host] " + msg);
                        results[fileIndex] = $"FAIL  {input}{Environment.NewLine}      {msg}";
                        UpdateOverallFromWorker(fileIndex, ProgressStage.Finalizing, 100);
                        return;
                    }
                    var plugin = handle.Instance;

                    var jobId = Guid.NewGuid().ToString("N");
                    var tempJobDir = Path.Combine(Path.GetTempPath(), "ConverTool", jobId, index.ToString());
                    Directory.CreateDirectory(tempJobDir);

                    var targetFormatId = SelectedTargetFormat?.Id
                                         ?? entry.Manifest.SupportedTargetFormats.FirstOrDefault()?.Id
                                         ?? "txt";

                    var selectedConfig = ConfigFields.ToDictionary(f => f.Key, f => f.GetValue(), StringComparer.OrdinalIgnoreCase);

                    string? finalPath = null;
                    string? fail = null;

                    var reporter = new VmReporter(
                        onLog: line => AppendLog($"[{index}/{total}] {line}"),
                        onProgress: info =>
                        {
                            var perFile = MapToOverallPercent(info.Stage, info.PercentWithinStage);
                            UpdateOverallFromWorker(fileIndex, info.Stage, perFile);
                        },
                        onCompleted: info =>
                        {
                            var tempOut = Path.Combine(tempJobDir, info.OutputRelativePath);
                            try
                            {
                                finalPath = MoveToFinalOutput(tempOut, input, targetFormatId, index);
                            }
                            catch (Exception ex)
                            {
                                fail = $"Move failed: {ex.GetType().Name}: {ex.Message}";
                                AppendLog("[host] " + fail);
                            }
                        },
                        onFailed: info => fail = info.ErrorMessage
                    );

                    AppendLog($"[host] Running ({index}/{total}) pluginId={entry.Manifest.PluginId}");

                    try
                    {
                        await plugin.ExecuteAsync(
                            new ExecuteContext(
                                JobId: jobId,
                                InputPath: input,
                                TempJobDir: tempJobDir,
                                TargetFormatId: targetFormatId,
                                SelectedConfig: selectedConfig,
                                Locale: _hostI18n.Locale,
                                OutputNamingContext: new Dictionary<string, object?>
                                {
                                    ["base"] = Path.GetFileNameWithoutExtension(input),
                                    ["index"] = index,
                                    ["ext"] = targetFormatId
                                }
                            ),
                            reporter,
                            ct
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        fail = "Canceled";
                        AppendLog($"[host] Canceled ({index}/{total}).");
                    }
                    catch (Exception ex)
                    {
                        fail = $"{ex.GetType().Name}: {ex.Message}";
                        AppendLog("[host] Exception: " + fail);
                    }
                    finally
                    {
                        if (!KeepTemp)
                        {
                            TryDeleteDirectory(tempJobDir);
                        }
                    }

                    results[fileIndex] = finalPath is null
                        ? $"FAIL  {input}{Environment.NewLine}      {fail ?? "Unknown error"}"
                        : $"OK    {input}{Environment.NewLine}      {finalPath}";
                    UpdateOverallFromWorker(fileIndex, ProgressStage.Finalizing, 100);
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    UpdateBatch(done, total, "Parallel");
                    semaphore.Release();
                }
            }, ct));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        Dispatcher.UIThread.Post(() =>
        {
            Progress = 100;
            ProgressText = "100% · Done";
            OutputText = string.Join(Environment.NewLine, results.Where(r => r is not null)!);
        });
    }

    private Task TogglePauseAsync()
    {
        _paused = !_paused;
        if (_paused)
        {
            _pauseGate.Reset();
            AppendLog("[host] Paused (will take effect before next file).");
        }
        else
        {
            _pauseGate.Set();
            AppendLog("[host] Resumed.");
        }

        return Task.CompletedTask;
    }

    private Task StopAsync()
    {
        // If user paused, workers may be blocked on _pauseGate; release so they can observe cancellation.
        _paused = false;
        _pauseGate.Set();
        _runCts?.Cancel();
        return Task.CompletedTask;
    }

    private Task WaitIfPausedAsync(CancellationToken ct)
    {
        if (_pauseGate.IsSet)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() => _pauseGate.Wait(ct), ct);
    }

    private void AppendLog(string line)
    {
        EnqueueLog(line);
    }

    private const int MaxLogLines = 2000;
    private readonly ConcurrentQueue<string> _pendingLogLines = new();
    private readonly List<string> _logLines = new();
    private DispatcherTimer? _logFlushTimer;

    private void EnqueueLog(string line)
    {
        _pendingLogLines.Enqueue(line);
        Dispatcher.UIThread.Post(EnsureLogFlushTimer);
    }

    private void EnsureLogFlushTimer()
    {
        _logFlushTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => FlushLogs());
        if (!_logFlushTimer.IsEnabled)
        {
            _logFlushTimer.Start();
        }
    }

    private void FlushLogs()
    {
        var changed = false;
        while (_pendingLogLines.TryDequeue(out var line))
        {
            _logLines.Add(line);
            changed = true;
        }

        if (!changed)
        {
            _logFlushTimer?.Stop();
            return;
        }

        if (_logLines.Count > MaxLogLines)
        {
            _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        }

        ProcessLog = string.Join(Environment.NewLine, _logLines);
    }

    public async Task BrowsePathAsync(PathFieldVm field)
    {
        if (TopLevel?.StorageProvider is null)
        {
            return;
        }

        if (string.Equals(field.Kind, "Folder", StringComparison.OrdinalIgnoreCase))
        {
            var folders = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            var folder = folders.FirstOrDefault();
            if (folder is not null)
            {
                field.Path = folder.Path.LocalPath;
            }
        }
        else
        {
            var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
            var file = files.FirstOrDefault();
            if (file is not null)
            {
                field.Path = file.Path.LocalPath;
            }
        }
    }

    private async Task BrowseInputAsync()
    {
        if (TopLevel?.StorageProvider is null)
        {
            return;
        }

        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true
        });

        AddInputPaths(files.Select(f => f.Path.LocalPath));
    }

    private async Task BrowseOutputDirAsync()
    {
        if (TopLevel?.StorageProvider is null)
        {
            return;
        }

        var folders = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is not null)
        {
            OutputDir = folder.Path.LocalPath;
        }
    }

    private Task BrowseConfigPathAsync(PathFieldVm? field)
    {
        if (field is null)
        {
            return Task.CompletedTask;
        }

        return BrowsePathAsync(field);
    }

    private async Task AddPluginAsync()
    {
        if (TopLevel?.StorageProvider is null)
        {
            return;
        }

        if (!CanEdit)
        {
            return;
        }

        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = _hostI18n.T("host/pluginManager/addPickerTitle"),
            FileTypeFilter =
            [
                new FilePickerFileType("ConverTool plugin (.zip)") { Patterns = ["*.zip"] },
            ],
        });

        var zip = files.FirstOrDefault();
        if (zip is null)
        {
            return;
        }

        var zipPath = zip.Path.LocalPath;
        var result = await PluginZipInstaller.InstallFromZipAsync(zipPath, AppContext.BaseDirectory);
        if (!result.Ok)
        {
            var msg = LocalizeInstallError(result);
            AppendLog("[host] " + msg);
            if (ShowErrorDialogAsync is not null)
            {
                await ShowErrorDialogAsync(msg);
            }
            return;
        }

        AppendLog($"[host] Plugin added: {result.PluginId}");
        ReloadCatalog();
    }

    /// <summary>Rescan <c>plugins/</c> from disk (e.g. after plugin manager add/delete). Keeps <see cref="AppServices.Plugins"/> in sync.</summary>
    public void RefreshPluginsFromDisk()
    {
        ReloadCatalog();
    }

    private void ReloadCatalog()
    {
        _catalog = PluginCatalog.LoadFromOutput(AppContext.BaseDirectory);
        AppServices.Plugins = _catalog;
        HasPlugins = _catalog.Plugins.Count > 0;
        ConfigPlaceholder = HasPlugins
            ? (HasAnyInputPaths()
                ? _hostI18n.T("host/config/placeholder")
                : _hostI18n.T("host/config/activateByInput"))
            : _hostI18n.T("host/config/noPluginHint");
        RaisePropertyChanged(nameof(ConfigPlaceholder));

        ReloadPluginContext();
    }

    public void AddInputPaths(IEnumerable<string> paths)
    {
        var current = (InputPaths ?? "")
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        foreach (var p in paths)
        {
            var trimmed = (p ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (!current.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                current.Add(trimmed);
            }
        }

        InputPaths = string.Join(Environment.NewLine, current);
    }

    private void UpdateBatch(int completed, int total, string stage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BatchText = total <= 0 ? "" : $"{_hostI18n.T("host/process/batchLabel")}: {completed}/{total} · {stage}";
        });
    }

    private string MoveToFinalOutput(string tempOutputPath, string inputPath, string targetExt, int index)
    {
        var outputDir = UseInputDirAsOutput
            ? (Path.GetDirectoryName(inputPath) ?? "")
            : (OutputDir ?? "").Trim();
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = Path.Combine(AppContext.BaseDirectory, "output");
        }
        Directory.CreateDirectory(outputDir);

        var template = (NamingTemplate ?? "{base}.{ext}").Trim();
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "{base}.{ext}";
        }

        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var fileName = template
            .Replace("{base}", baseName, StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", targetExt, StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", index.ToString(), StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
        {
            fileName = $"{fileName}.{targetExt}";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        var candidate = Path.Combine(outputDir, fileName);
        var finalPath = ResolveConflict(candidate);

        try
        {
            File.Move(tempOutputPath, finalPath, overwrite: false);
        }
        catch (IOException)
        {
            File.Copy(tempOutputPath, finalPath, overwrite: false);
            File.Delete(tempOutputPath);
        }

        if (!string.Equals(finalPath, candidate, StringComparison.OrdinalIgnoreCase))
        {
            AppendLog($"[host] Name conflict: renamed to {Path.GetFileName(finalPath)}");
        }

        return finalPath;
    }

    private static string ResolveConflict(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_({i}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to find non-conflicting output filename.");
    }

    private static int MapToOverallPercent(ProgressStage stage, int? percentWithinStage)
    {
        var p = Math.Clamp(percentWithinStage ?? 0, 0, 100);
        return stage switch
        {
            ProgressStage.Preparing => (int)Math.Round(0 + 10 * (p / 100.0)),
            ProgressStage.Running => (int)Math.Round(10 + 80 * (p / 100.0)),
            ProgressStage.Finalizing => (int)Math.Round(90 + 10 * (p / 100.0)),
            _ => p
        };
    }

    private static int MapBatchToOverallPercent(int completedFilesBeforeThis, int totalFiles, int currentFilePercent)
    {
        if (totalFiles <= 0)
        {
            return currentFilePercent;
        }

        var completed = Math.Clamp(completedFilesBeforeThis, 0, totalFiles);
        var perFile = Math.Clamp(currentFilePercent, 0, 100);
        var overall = ((completed + perFile / 100.0) / totalFiles) * 100.0;
        return (int)Math.Round(Math.Clamp(overall, 0, 100));
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string? TryGetStringDefault(JsonElement? el)
    {
        if (el is null)
        {
            return null;
        }

        return el.Value.ValueKind switch
        {
            JsonValueKind.String => el.Value.GetString(),
            JsonValueKind.Number => el.Value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.Value.ToString()
        };
    }

    private static double TryGetDoubleDefault(JsonElement? el, double fallback)
    {
        if (el is null)
        {
            return fallback;
        }

        try
        {
            return el.Value.ValueKind switch
            {
                JsonValueKind.Number => el.Value.GetDouble(),
                JsonValueKind.String => double.TryParse(el.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
                    ? d
                    : fallback,
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
    }

    private string LocalizeInstallError(PluginZipInstaller.Result result)
    {
        var key = result.ErrorCode switch
        {
            PluginZipInstaller.ErrorCodes.InvalidZip => "host/pluginInstall/error/invalidZip",
            PluginZipInstaller.ErrorCodes.ManifestNotFound => "host/pluginInstall/error/manifestNotFound",
            PluginZipInstaller.ErrorCodes.ManifestNotUnique => "host/pluginInstall/error/manifestNotUnique",
            PluginZipInstaller.ErrorCodes.ManifestInvalid => "host/pluginInstall/error/manifestInvalid",
            PluginZipInstaller.ErrorCodes.MissingTerminationSupport => "host/pluginInstall/error/missingTermination",
            PluginZipInstaller.ErrorCodes.FilesInUse => "host/pluginInstall/error/filesInUse",
            PluginZipInstaller.ErrorCodes.FilesLocked => "host/pluginInstall/error/filesLocked",
            _ => "host/pluginInstall/error/unknown",
        };

        var msg = _hostI18n.T(key);
        if (!string.IsNullOrWhiteSpace(result.Details) &&
            (result.ErrorCode == PluginZipInstaller.ErrorCodes.Unknown ||
             result.ErrorCode == PluginZipInstaller.ErrorCodes.ManifestNotUnique))
        {
            msg += Environment.NewLine + result.Details;
        }

        return msg;
    }
}

public sealed record TargetFormatVm(string Id, string Text)
{
    public override string ToString() => Text;
}

internal sealed class VmReporter : IProgressReporter
{
    private readonly Action<string> _onLog;
    private readonly Action<ProgressInfo> _onProgress;
    private readonly Action<CompletedInfo> _onCompleted;
    private readonly Action<FailedInfo> _onFailed;

    public VmReporter(Action<string> onLog, Action<ProgressInfo> onProgress, Action<CompletedInfo> onCompleted, Action<FailedInfo> onFailed)
    {
        _onLog = onLog;
        _onProgress = onProgress;
        _onCompleted = onCompleted;
        _onFailed = onFailed;
    }

    public void OnLog(string line) => _onLog(line);
    public void OnProgress(ProgressInfo info) => _onProgress(info);
    public void OnCompleted(CompletedInfo info) => _onCompleted(info);
    public void OnFailed(FailedInfo info) => _onFailed(info);
}

