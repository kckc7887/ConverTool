using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Host.Plugins;
using PluginAbstractions;

namespace Host.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private PluginCatalog _catalog;
    private readonly PluginI18nService _pluginI18n;
    private readonly I18nService _hostI18n;

    private PluginEntry? _activePlugin;

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

        Languages = new ObservableCollection<string>(I18nService.SupportedLocales);
        SelectedLanguage = _hostI18n.Locale;

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
            ? _hostI18n.T("host/config/placeholder")
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
        StopLabel = _hostI18n.T("host/process/stop");
        EnableParallelLabel = _hostI18n.T("host/process/parallelEnable");
        ParallelismLabel = _hostI18n.T("host/process/parallelism");

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        OutputDir = string.IsNullOrWhiteSpace(docs)
            ? Path.Combine(AppContext.BaseDirectory, "output")
            : Path.Combine(docs, "ConverToolOutput");
        NamingTemplate = "{base}.{ext}";
        InputPaths = "";

        StartCommand = new AsyncCommand(StartAsync);
        BrowseInputCommand = new AsyncCommand(BrowseInputAsync);
        BrowseOutputDirCommand = new AsyncCommand(BrowseOutputDirAsync);
        BrowseConfigPathCommand = new AsyncCommand<PathFieldVm>(BrowseConfigPathAsync);
        PauseCommand = new AsyncCommand(TogglePauseAsync);
        StopCommand = new AsyncCommand(StopAsync);
        AddPluginCommand = new AsyncCommand(AddPluginAsync);

        _hostI18n.LocaleChanged += (_, _) => ReloadHostStrings();
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
    public string StopLabel { get; private set; } = "";
    public string EnableParallelLabel { get; private set; } = "";
    public string ParallelismLabel { get; private set; } = "";
    public string LanguageLabel { get; private set; } = "";

    // ---- inputs ----
    private string _inputPaths = "";
    public string InputPaths { get => _inputPaths; set { if (SetProperty(ref _inputPaths, value)) ReloadPluginContext(); } }

    private string _outputDir = "";
    public string OutputDir { get => _outputDir; set => SetProperty(ref _outputDir, value); }

    private bool _useInputDirAsOutput;
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
            ? _hostI18n.T("host/config/placeholder")
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
        RaisePropertyChanged(nameof(StopLabel));
        RaisePropertyChanged(nameof(EnableParallelLabel));
        RaisePropertyChanged(nameof(ParallelismLabel));

        // plugin strings depend on locale too
        ReloadPluginContext();
    }

    private void ReloadPluginContext()
    {
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
        if (schema?.Sections is null)
        {
            return;
        }

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
                    _ => new TextFieldVm(field.Key, label, help, TryGetStringDefault(field.DefaultValue))
                };

                ConfigFields.Add(vm);
            }
        }
    }

    private PluginEntry? ResolveActivePlugin()
    {
        var firstInput = (InputPaths ?? "")
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        if (string.IsNullOrWhiteSpace(firstInput))
        {
            return _catalog.Plugins.FirstOrDefault();
        }

        return PluginRouter.RouteByInputPath(_catalog, firstInput) ?? _catalog.Plugins.FirstOrDefault();
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
            AppendLog("[host] Paused (will pause before next file).");
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
            AllowMultiple = false
        });

        var zip = files.FirstOrDefault();
        if (zip is null)
        {
            return;
        }

        var zipPath = zip.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(zipPath) || !zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("[host] Please select a .zip file.");
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
                AppendLog("[host] Invalid plugin zip: manifest.json not found.");
                return;
            }

            if (manifestPaths.Length != 1)
            {
                AppendLog($"[host] Invalid plugin zip: expected exactly 1 manifest.json, found {manifestPaths.Length}.");
                return;
            }

            var manifestPath = manifestPaths[0];
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifestModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.PluginId))
            {
                AppendLog("[host] Invalid plugin manifest.json.");
                return;
            }

            if (!manifest.SupportsTerminationOnCancel)
            {
                AppendLog($"[host] Skip plugin '{manifest.PluginId}': requires supportsTerminationOnCancel=true.");
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
            AppendLog($"[host] Plugin added: {manifest.PluginId}");

            ReloadCatalog();
        }
        catch (Exception ex)
        {
            AppendLog($"[host] Add plugin failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private void ReloadCatalog()
    {
        _catalog = PluginCatalog.LoadFromOutput(AppContext.BaseDirectory);
        HasPlugins = _catalog.Plugins.Count > 0;
        ConfigPlaceholder = HasPlugins
            ? _hostI18n.T("host/config/placeholder")
            : _hostI18n.T("host/config/noPluginHint");
        RaisePropertyChanged(nameof(ConfigPlaceholder));

        ReloadPluginContext();
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        // Copy directory contents (not the parent folder itself).
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

