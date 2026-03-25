using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Host.Plugins;
using Host.Settings;
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
        nameof(EnableContextMenu),
        nameof(SelectedTargetFormat),
    };

    private PluginCatalog _catalog;
    private readonly PluginI18nService _pluginI18n;
    private readonly I18nService _hostI18n;

    private PluginEntry? _activePlugin;

    private UserSettingsFile _userSettings = new();
    private Dictionary<string, SettingItem> _settings = new();
    private Dictionary<string, Dictionary<string, SettingItem>> _pluginSettings = new();
    private bool _loadingUserSettings;
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

    private readonly NamingTemplateViewModel _namingTemplateViewModel;
    public NamingTemplateViewModel NamingTemplateViewModel => _namingTemplateViewModel;

    private readonly InputFileViewModel _inputFileViewModel;
    public InputFileViewModel InputFileViewModel => _inputFileViewModel;

    public MainWindowViewModel(PluginCatalog catalog, PluginI18nService pluginI18n, I18nService hostI18n)
    {
        _catalog = catalog;
        HasPlugins = _catalog.Plugins.Count > 0;
        HasActivePlugin = false;
        _pluginI18n = pluginI18n;
        _hostI18n = hostI18n;

        _namingTemplateViewModel = new NamingTemplateViewModel();
        _namingTemplateViewModel.NamingTemplateChanged += (s, e) =>
        {
            if (!_loadingUserSettings)
            {
                _userSettings.NamingTemplate = _namingTemplateViewModel.NamingTemplate;
                var (custom1, custom2, custom3) = _namingTemplateViewModel.GetCustomTokenTexts();
                _userSettings.CustomToken1Text = custom1;
                _userSettings.CustomToken2Text = custom2;
                _userSettings.CustomToken3Text = custom3;
                ScheduleSaveUserSettings();
            }
        };

        var allSupportedExtensions = GetAllSupportedExtensions(catalog);
        _inputFileViewModel = new InputFileViewModel(allSupportedExtensions);
        _inputFileViewModel.InputFilesChanged += (s, e) =>
        {
            RaisePropertyChanged(nameof(HasInputFiles));
            RaisePropertyChanged(nameof(HasNoInputFiles));
            if (!_loadingUserSettings)
            {
                UpdateConfigPlaceholder();
                ReloadPluginContext();
            }
        };
        _inputFileViewModel.UnsupportedFormatDetected += (s, e) =>
        {
            var files = string.Join(", ", e.UnsupportedFiles);
            var msg = _hostI18n.T("host/input/unsupportedFormat");
            AppendLog($"[host] {msg}: {files}");
        };

        // 加载本体配置（只从配置文件读取）
        _settings = SettingManager.LoadHostSettings();
        
        // 从配置文件读取设置（Value为空时使用Default）
        _userSettings = new UserSettingsFile
        {
            Locale = SettingManager.GetValue<string>(_settings, "Locale"),
            OutputDir = SettingManager.GetValue<string>(_settings, "OutputDir"),
            UseInputDirAsOutput = SettingManager.GetValue<bool>(_settings, "UseInputDirAsOutput"),
            NamingTemplate = SettingManager.GetValue<string>(_settings, "NamingTemplate"),
            CustomToken1Text = SettingManager.GetValue<string>(_settings, "CustomToken1Text"),
            CustomToken2Text = SettingManager.GetValue<string>(_settings, "CustomToken2Text"),
            CustomToken3Text = SettingManager.GetValue<string>(_settings, "CustomToken3Text"),
            EnableParallelProcessing = SettingManager.GetValue<bool>(_settings, "EnableParallelProcessing"),
            Parallelism = SettingManager.GetValue<int>(_settings, "Parallelism"),
            KeepTemp = SettingManager.GetValue<bool>(_settings, "KeepTemp"),
            EnableContextMenu = SettingManager.GetValue<bool>(_settings, "EnableContextMenu"),
            AllowedSourceExtensions = SettingManager.GetValue<List<string>>(_settings, "AllowedSourceExtensions"),
            AllowedTargetFormats = SettingManager.GetValue<Dictionary<string, List<string>>>(_settings, "AllowedTargetFormats"),
            Plugins = SettingManager.GetValue<Dictionary<string, PluginUserSettings>>(_settings, "Plugins")
        };

        // 加载所有插件配置（与 PluginCatalog 中各插件目录下的 config.json 一致）
        _pluginSettings = SettingManager.LoadAllPluginSettings(_catalog);
        
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
        InputRemoveLabel = _hostI18n.T("host/input/remove");
        InputClearAllLabel = _hostI18n.T("host/input/clearAll");
        OutputBrowseLabel = _hostI18n.T("host/output/browse");
        UpdateConfigPlaceholder();
        AddPluginLabel = _hostI18n.T("host/app/addPlugin");
        ManagePluginsLabel = _hostI18n.T("host/app/managePlugins");
        NoPluginHintLabel = _hostI18n.T("host/config/noPluginHint");
        ProcessPlaceholder = _hostI18n.T("host/process/placeholder");
        OutputPlaceholder = _hostI18n.T("host/output/placeholder");
        TargetFormatLabel = _hostI18n.T("host/config/targetFormat");
        OutputDirLabel = _hostI18n.T("host/output/dirLabel");
        NamingTemplateLabel = _hostI18n.T("host/output/templateLabel");
        NamingTemplateHelp = _hostI18n.T("host/output/templateHelp");
        NamingTemplateExtSuffixPrefix = _hostI18n.T("host/output/targetExtSuffixPrefix");
        UseInputDirLabel = _hostI18n.T("host/output/useInputDir");
        KeepTempLabel = _hostI18n.T("host/process/keepTemp");
        StartLabel = _hostI18n.T("host/process/start");
        PauseLabel = _hostI18n.T("host/process/pause");
        PauseTooltipLabel = _hostI18n.T("host/process/pauseTooltip");
        StopLabel = _hostI18n.T("host/process/stop");
        EnableParallelLabel = _hostI18n.T("host/process/parallelEnable");
        ParallelismLabel = _hostI18n.T("host/process/parallelism");
        ProcessLogSectionLabel = _hostI18n.T("host/process/logSection");

        _loadingUserSettings = true;
        try
        {
            // 须先于 SetTokenTexts：从 config 读入的自定义标签要写入字段并同步子 VM，否则 TextBox 绑定拿不到值
            ApplyCustomTokensFromUserSettingsToUi();
            ApplyNamingTemplateHostI18n();
        }
        finally
        {
            _loadingUserSettings = false;
        }

        _loadingUserSettings = true;
        try
        {
            // 只从配置文件读取值，禁止代码默认值（OutputDir 支持 %USERPROFILE% 等环境变量）
            OutputDir = ExpandOutputPath(_userSettings.OutputDir);
            UseInputDirAsOutput = _userSettings.UseInputDirAsOutput ?? true;
            _namingTemplateViewModel.NamingTemplate = _userSettings.NamingTemplate ?? "";
            EnableParallelProcessing = _userSettings.EnableParallelProcessing ?? false;
            Parallelism = _userSettings.Parallelism ?? 0;
            KeepTemp = _userSettings.KeepTemp ?? false;
            EnableContextMenu = _userSettings.EnableContextMenu ?? false;
            AllowedSourceExtensions = _userSettings.AllowedSourceExtensions ?? new List<string>();
            AllowedTargetFormats = _userSettings.AllowedTargetFormats ?? new Dictionary<string, List<string>>();
        }
        finally
        {
            _loadingUserSettings = false;
        }

        // 更新右键菜单状态
        if (EnableContextMenu)
        {
            UpdateContextMenu();
        }

        _namingTemplateViewModel.Initialize(_namingTemplateViewModel.NamingTemplate);

        StartCommand = new AsyncCommand(StartAsync);
        BrowseInputCommand = new AsyncCommand(() => _inputFileViewModel.BrowseInputAsync());
        ClearAllInputCommand = new SyncCommand(() => _inputFileViewModel.Clear());
        BrowseOutputDirCommand = new AsyncCommand(BrowseOutputDirAsync);
        BrowseConfigPathCommand = new AsyncCommand<PathFieldVm>(BrowseConfigPathAsync);
        PauseCommand = new AsyncCommand(TogglePauseAsync);
        StopCommand = new AsyncCommand(StopAsync);
        AddPluginCommand = new AsyncCommand(AddPluginAsync);
        AddNamingTemplateTokenCommand = new AsyncCommand<string>(v =>
        {
            _namingTemplateViewModel.ToggleNamingTemplateCandidate(v ?? "");
            return Task.CompletedTask;
        });
        SaveSettingsCommand = new SyncCommand(SaveSettings);

        _hostI18n.LocaleChanged += (_, _) => ReloadHostStrings();
        PropertyChanged += OnVmPersistablePropertyChanged;
        TargetFormats.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(HasTargetFormats));
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
    public string InputRemoveLabel { get; private set; } = "";
    public string InputClearAllLabel { get; private set; } = "";
    public string OutputBrowseLabel { get; private set; } = "";
    public string ConfigPlaceholder { get; private set; } = "";
    public string ProcessPlaceholder { get; private set; } = "";
    public string OutputPlaceholder { get; private set; } = "";
    public string TargetFormatLabel { get; private set; } = "";
    public string OutputDirLabel { get; private set; } = "";
    public string NamingTemplateLabel { get; private set; } = "";
    /// <summary>命名模板区「自定义标签」行标题（中英文由 Host locales 提供）。</summary>
    public string NamingTemplateCustomRowLabel { get; private set; } = "";
    public string CustomToken1Watermark { get; private set; } = "";
    public string CustomToken2Watermark { get; private set; } = "";
    public string CustomToken3Watermark { get; private set; } = "";
    public string NamingTemplateHelp { get; private set; } = "";
    public string NamingTemplateExtSuffixPrefix { get; private set; } = "";
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
    public ObservableCollection<InputFileItemVm> InputFiles => _inputFileViewModel.InputFiles;

    public bool HasInputFiles => _inputFileViewModel.HasInputFiles;

    public bool HasNoInputFiles => _inputFileViewModel.HasNoInputFiles;

    private string _outputDir = "";
    public string OutputDir { get => _outputDir; set => SetProperty(ref _outputDir, value); }

    private bool _useInputDirAsOutput = true;
    public bool UseInputDirAsOutput { get => _useInputDirAsOutput; set => SetProperty(ref _useInputDirAsOutput, value); }

    private bool _enableParallelProcessing;
    public bool EnableParallelProcessing { get => _enableParallelProcessing; set => SetProperty(ref _enableParallelProcessing, value); }

    private int _parallelism = 2;
    public int Parallelism { get => _parallelism; set => SetProperty(ref _parallelism, Math.Clamp(value, 1, 8)); }

    private string _customToken1Text = "";
    private string _customToken2Text = "";
    private string _customToken3Text = "";

    public string NamingTemplate
    {
        get => _namingTemplateViewModel.NamingTemplate;
        set
        {
            if (_namingTemplateViewModel.NamingTemplate != value)
            {
                _namingTemplateViewModel.NamingTemplate = value;
                RaisePropertyChanged();
            }
        }
    }

    public ObservableCollection<NamingTemplateTokenVm> NamingTemplateSelectedTags => _namingTemplateViewModel.NamingTemplateSelectedTags;

    public bool HasNamingTemplateBaseToken => _namingTemplateViewModel.HasNamingTemplateBaseToken;
    public bool HasNamingTemplateIndexToken => _namingTemplateViewModel.HasNamingTemplateIndexToken;
    public bool HasNamingTemplateTimeYmdToken => _namingTemplateViewModel.HasNamingTemplateTimeYmdToken;
    public bool HasNamingTemplateTimeHmsToken => _namingTemplateViewModel.HasNamingTemplateTimeHmsToken;
    public bool HasNamingTemplateCustom1Token => _namingTemplateViewModel.HasNamingTemplateCustom1Token;
    public bool HasNamingTemplateCustom2Token => _namingTemplateViewModel.HasNamingTemplateCustom2Token;
    public bool HasNamingTemplateCustom3Token => _namingTemplateViewModel.HasNamingTemplateCustom3Token;

    public string NamingTemplateTokenBaseText => _namingTemplateViewModel.NamingTemplateTokenBaseText;
    public string NamingTemplateTokenIndexText => _namingTemplateViewModel.NamingTemplateTokenIndexText;

    public string NamingTemplateBaseCandidateText => _namingTemplateViewModel.NamingTemplateBaseCandidateText;
    public string NamingTemplateBaseCandidateValue => _namingTemplateViewModel.NamingTemplateBaseCandidateValue;
    public string NamingTemplateIndexCandidateText => _namingTemplateViewModel.NamingTemplateIndexCandidateText;
    public string NamingTemplateIndexCandidateValue => _namingTemplateViewModel.NamingTemplateIndexCandidateValue;

    public string NamingTemplateTimeDateCandidateText => _namingTemplateViewModel.NamingTemplateTimeDateCandidateText;
    public string NamingTemplateTimeDateCandidateValue => _namingTemplateViewModel.NamingTemplateTimeDateCandidateValue;

    public string NamingTemplateTimeHmsCandidateText => _namingTemplateViewModel.NamingTemplateTimeHmsCandidateText;
    public string NamingTemplateTimeHmsCandidateValue => _namingTemplateViewModel.NamingTemplateTimeHmsCandidateValue;

    public string NamingTemplateCustom1CandidateText => _namingTemplateViewModel.NamingTemplateCustom1CandidateText;
    public string NamingTemplateCustom1CandidateValue => _namingTemplateViewModel.NamingTemplateCustom1CandidateValue;
    public string NamingTemplateCustom2CandidateText => _namingTemplateViewModel.NamingTemplateCustom2CandidateText;
    public string NamingTemplateCustom2CandidateValue => _namingTemplateViewModel.NamingTemplateCustom2CandidateValue;
    public string NamingTemplateCustom3CandidateText => _namingTemplateViewModel.NamingTemplateCustom3CandidateText;
    public string NamingTemplateCustom3CandidateValue => _namingTemplateViewModel.NamingTemplateCustom3CandidateValue;

    public ObservableCollection<string> NamingTemplateActionCandidates => _namingTemplateViewModel.NamingTemplateActionCandidates;

    // 自定义标签文本（双向绑定；字段为绑定真实来源，与子 VM 同步）
    public string CustomToken1Text
    {
        get => _customToken1Text;
        set
        {
            if (!SetProperty(ref _customToken1Text, value))
                return;
            _namingTemplateViewModel.SetCustomTokenTexts(_customToken1Text, _customToken2Text, _customToken3Text);
            RaiseCustomTokenChipCandidateTextsChanged();
            if (!_loadingUserSettings)
                ScheduleSaveUserSettings();
        }
    }

    public string CustomToken2Text
    {
        get => _customToken2Text;
        set
        {
            if (!SetProperty(ref _customToken2Text, value))
                return;
            _namingTemplateViewModel.SetCustomTokenTexts(_customToken1Text, _customToken2Text, _customToken3Text);
            RaiseCustomTokenChipCandidateTextsChanged();
            if (!_loadingUserSettings)
                ScheduleSaveUserSettings();
        }
    }

    public string CustomToken3Text
    {
        get => _customToken3Text;
        set
        {
            if (!SetProperty(ref _customToken3Text, value))
                return;
            _namingTemplateViewModel.SetCustomTokenTexts(_customToken1Text, _customToken2Text, _customToken3Text);
            RaiseCustomTokenChipCandidateTextsChanged();
            if (!_loadingUserSettings)
                ScheduleSaveUserSettings();
        }
    }

    /// <summary>
    /// 上方「自定义」ToggleButton 绑定的是 <see cref="NamingTemplateCustom1CandidateText"/> 等主 VM 属性；
    /// 子 VM 已通知，但不会冒泡到主 VM，需显式转发。
    /// </summary>
    private void RaiseCustomTokenChipCandidateTextsChanged()
    {
        RaisePropertyChanged(nameof(NamingTemplateCustom1CandidateText));
        RaisePropertyChanged(nameof(NamingTemplateCustom2CandidateText));
        RaisePropertyChanged(nameof(NamingTemplateCustom3CandidateText));
    }

    private bool _keepTemp;
    public bool KeepTemp { get => _keepTemp; set => SetProperty(ref _keepTemp, value); }

    private bool _enableContextMenu;
    public bool EnableContextMenu 
    {
        get => _enableContextMenu;
        set 
        { 
            if (SetProperty(ref _enableContextMenu, value))
            {
                if (!_loadingUserSettings)
                {
                    _userSettings.EnableContextMenu = value;
                    UpdateContextMenu();
                    ScheduleSaveUserSettings();
                }
            }
        }
    }

    private List<string> _allowedSourceExtensions = new();
    public List<string> AllowedSourceExtensions 
    {
        get => _allowedSourceExtensions;
        set 
        { 
            // 比较列表内容是否真正改变
            bool contentChanged = !ListsAreEqual(_allowedSourceExtensions, value);
            if (contentChanged && SetProperty(ref _allowedSourceExtensions, value))
            {
                if (!_loadingUserSettings)
                {
                    _userSettings.AllowedSourceExtensions = value;
                    if (_enableContextMenu)
                    {
                        UpdateContextMenu();
                    }
                    ScheduleSaveUserSettings();
                }
            }
        }
    }
    
    private bool ListsAreEqual(List<string>? list1, List<string>? list2)
    {
        if (list1 == list2) return true;
        if (list1 == null || list2 == null) return false;
        if (list1.Count != list2.Count) return false;
        
        // 排序后比较
        var sortedList1 = list1.OrderBy(s => s).ToList();
        var sortedList2 = list2.OrderBy(s => s).ToList();
        
        for (int i = 0; i < sortedList1.Count; i++)
        {
            if (sortedList1[i] != sortedList2[i])
                return false;
        }
        
        return true;
    }

    private Dictionary<string, List<string>> _allowedTargetFormats = new();
    public Dictionary<string, List<string>> AllowedTargetFormats 
    {
        get => _allowedTargetFormats;
        set 
        { 
            if (SetProperty(ref _allowedTargetFormats, value))
            {
                if (!_loadingUserSettings)
                {
                    _userSettings.AllowedTargetFormats = value;
                    ScheduleSaveUserSettings();
                }
            }
        }
    }

    // ---- plugin-driven UI ----
    public ObservableCollection<TargetFormatVm> TargetFormats { get; } = new();

    private TargetFormatVm? _selectedTargetFormat;

    /// <summary>当前选中的目标格式（须为 <see cref="TargetFormats"/> 中的实例）。</summary>
    public TargetFormatVm? SelectedTargetFormat
    {
        get => _selectedTargetFormat;
        set
        {
            if (!SetProperty(ref _selectedTargetFormat, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(SelectedTargetFormatIdDisplay));
            RaisePropertyChanged(nameof(SelectedTargetFormatDisplayText));
            RaisePropertyChanged(nameof(NamingTemplateExtSuffix));
            RaisePropertyChanged(nameof(HasNamingTemplateExtSuffix));
            if (!_isReevaluatingUiRules && !_loadingUserSettings)
            {
                ReevaluateUiRules(changedFieldKey: null);
            }
        }
    }

    /// <summary>当前选中项的 <c>id</c>（manifest <c>supportedTargetFormats[].id</c>）。</summary>
    public string SelectedTargetFormatIdDisplay => SelectedTargetFormat?.Id ?? "";

    /// <summary>折叠态展示：<c>displayNameKey</c> 解析后的文案（与 <see cref="TargetFormatVm.Text"/> 一致）。</summary>
    public string SelectedTargetFormatDisplayText => SelectedTargetFormat?.Text ?? "";

    /// <summary>当前插件下是否存在至少一个可选目标格式（由输入扩展名与 visibleIf 过滤后）。</summary>
    public bool HasTargetFormats => TargetFormats.Count > 0;

    public string NamingTemplateExtSuffix
    {
        get
        {
            var id = SelectedTargetFormat?.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                return "";
            }

            var suffix = "." + id;
            var cur = (_namingTemplateViewModel.NamingTemplate ?? "").Trim().TrimEnd('.');
            if (!string.IsNullOrWhiteSpace(cur) && cur.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return suffix;
        }
    }

    public bool HasNamingTemplateExtSuffix => !string.IsNullOrWhiteSpace(NamingTemplateExtSuffix);

    public ObservableCollection<ConfigFieldVm> ConfigFields { get; } = new();

    // ---- UI rules (visibleIf / dependencies) ----
    private readonly Dictionary<string, VisibleIfModel?> _visibleIfByFieldKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string[]?> _visibleForInputExtensionsByFieldKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string[]?> _visibleForTargetFormatsByFieldKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<FieldBoolRelationModel> _fieldBoolRelations = new();

    private IReadOnlyList<TargetFormatModel> _allTargetFormatModels = Array.Empty<TargetFormatModel>();

    private readonly Dictionary<string, string> _baseLabelByFieldKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string?> _baseHelpByFieldKey =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _isReevaluatingUiRules;
    private bool _isConvertingTargetSizeUnit;
    private string? _lastTargetSizeUnitId;

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
    public ICommand ClearAllInputCommand { get; }
    public AsyncCommand BrowseOutputDirCommand { get; }
    public AsyncCommand<PathFieldVm> BrowseConfigPathCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand AddPluginCommand { get; }
    public AsyncCommand<string> AddNamingTemplateTokenCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    // Host will set this from code-behind (TopLevel required for picker).
    public TopLevel? TopLevel
    {
        get => _inputFileViewModel.TopLevel;
        set => _inputFileViewModel.TopLevel = value;
    }
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

    private void ApplyNamingTemplateHostI18n()
    {
        _namingTemplateViewModel.SetTokenTexts(
            _hostI18n.T("host/output/namingTemplateTokenBaseText"),
            _hostI18n.T("host/output/namingTemplateTokenIndexText"),
            _hostI18n.T("host/output/namingTemplateTokenTimeDateText"),
            _hostI18n.T("host/output/namingTemplateTokenTimeHmsText"));
        _namingTemplateViewModel.SetCustomTokenDefaultLabels(
            _hostI18n.T("host/output/namingTemplateCustom1Default"),
            _hostI18n.T("host/output/namingTemplateCustom2Default"),
            _hostI18n.T("host/output/namingTemplateCustom3Default"));

        NamingTemplateCustomRowLabel = _hostI18n.T("host/output/namingTemplateCustomRowLabel");
        CustomToken1Watermark = _hostI18n.T("host/output/namingTemplateCustom1Watermark");
        CustomToken2Watermark = _hostI18n.T("host/output/namingTemplateCustom2Watermark");
        CustomToken3Watermark = _hostI18n.T("host/output/namingTemplateCustom3Watermark");
        RaisePropertyChanged(nameof(NamingTemplateCustomRowLabel));
        RaisePropertyChanged(nameof(CustomToken1Watermark));
        RaisePropertyChanged(nameof(CustomToken2Watermark));
        RaisePropertyChanged(nameof(CustomToken3Watermark));

        // 触发芯片文本属性变更通知（基础标签和自定义标签）
        RaisePropertyChanged(nameof(NamingTemplateBaseCandidateText));
        RaisePropertyChanged(nameof(NamingTemplateIndexCandidateText));
        RaisePropertyChanged(nameof(NamingTemplateTimeDateCandidateText));
        RaisePropertyChanged(nameof(NamingTemplateTimeHmsCandidateText));
        RaiseCustomTokenChipCandidateTextsChanged();
    }

    private void UpdateConfigPlaceholder()
    {
        ConfigPlaceholder = HasPlugins
            ? (HasAnyInputPaths()
                ? _hostI18n.T("host/config/placeholder")
                : _hostI18n.T("host/config/activateByInput"))
            : _hostI18n.T("host/config/noPluginHint");
        RaisePropertyChanged(nameof(ConfigPlaceholder));
    }

    private void ReloadHostStrings()
    {
        _pluginI18n.ClearCache();

        HostTitle = _hostI18n.T("host/app/title");
        LanguageLabel = _hostI18n.T("host/app/language");

        InputHeader = _hostI18n.T("host/section/input");
        ConfigHeader = _hostI18n.T("host/section/config");
        ProcessHeader = _hostI18n.T("host/section/process");
        OutputHeader = _hostI18n.T("host/section/output");

        InputPlaceholder = _hostI18n.T("host/input/placeholder");
        InputBrowseLabel = _hostI18n.T("host/input/browse");
        InputRemoveLabel = _hostI18n.T("host/input/remove");
        InputClearAllLabel = _hostI18n.T("host/input/clearAll");
        OutputBrowseLabel = _hostI18n.T("host/output/browse");
        UpdateConfigPlaceholder();
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

        ApplyNamingTemplateHostI18n();

        RaisePropertyChanged(nameof(LanguageLabel));
        RaisePropertyChanged(nameof(InputHeader));
        RaisePropertyChanged(nameof(ConfigHeader));
        RaisePropertyChanged(nameof(ProcessHeader));
        RaisePropertyChanged(nameof(OutputHeader));
        RaisePropertyChanged(nameof(InputPlaceholder));
        RaisePropertyChanged(nameof(InputBrowseLabel));
        RaisePropertyChanged(nameof(InputRemoveLabel));
        RaisePropertyChanged(nameof(InputClearAllLabel));
        RaisePropertyChanged(nameof(OutputBrowseLabel));
        RaisePropertyChanged(nameof(AddPluginLabel));
        RaisePropertyChanged(nameof(ManagePluginsLabel));
        RaisePropertyChanged(nameof(NoPluginHintLabel));
        RaisePropertyChanged(nameof(ProcessPlaceholder));
        RaisePropertyChanged(nameof(OutputPlaceholder));
        RaisePropertyChanged(nameof(TargetFormatLabel));
        RaisePropertyChanged(nameof(OutputDirLabel));
        RaisePropertyChanged(nameof(NamingTemplateLabel));
        RaisePropertyChanged(nameof(NamingTemplateCustomRowLabel));
        RaisePropertyChanged(nameof(CustomToken1Watermark));
        RaisePropertyChanged(nameof(CustomToken2Watermark));
        RaisePropertyChanged(nameof(CustomToken3Watermark));
        RaisePropertyChanged(nameof(NamingTemplateHelp));
        RaisePropertyChanged(nameof(NamingTemplateExtSuffixPrefix));
        RaisePropertyChanged(nameof(UseInputDirLabel));
        RaisePropertyChanged(nameof(KeepTempLabel));
        RaisePropertyChanged(nameof(StartLabel));
        RaisePropertyChanged(nameof(PauseLabel));
        RaisePropertyChanged(nameof(PauseTooltipLabel));
        RaisePropertyChanged(nameof(StopLabel));
        RaisePropertyChanged(nameof(ProcessLogSectionLabel));
        RaisePropertyChanged(nameof(EnableParallelLabel));
        RaisePropertyChanged(nameof(ParallelismLabel));

        ReloadPluginContext();
    }

    public void MoveNamingTemplateToken(NamingTemplateTokenVm from, NamingTemplateTokenVm to)
    {
        _namingTemplateViewModel.MoveNamingTemplateToken(from, to);
    }

    private void ReloadPluginContext()
    {
        DetachConfigFieldHandlers();

        if (_activePlugin?.Manifest?.PluginId is { } prevPid)
            PersistCurrentPluginUiToStores(prevPid);

        _activePlugin = ResolveActivePlugin();
        var plugin = _activePlugin;
        HasActivePlugin = plugin?.Manifest is not null;

        TargetFormats.Clear();
        ConfigFields.Clear();
        _visibleIfByFieldKey.Clear();
        _visibleForInputExtensionsByFieldKey.Clear();
        _visibleForTargetFormatsByFieldKey.Clear();
        _fieldBoolRelations.Clear();
        _baseLabelByFieldKey.Clear();
        _baseHelpByFieldKey.Clear();

        if (!HasActivePlugin)
        {
            SelectedTargetFormat = null;
        }
        else
        {
        _allTargetFormatModels = plugin!.Manifest.SupportedTargetFormats ?? Array.Empty<TargetFormatModel>();
        TargetFormats.Clear();
        SelectedTargetFormat = null;

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
                        "Checkbox" => new CheckboxFieldVm(
                            field.Key,
                            label,
                            help,
                            defaultValue: TryGetBoolDefault(field.DefaultValue, false)),
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

                    _baseLabelByFieldKey[field.Key] = vm.Label;
                    _baseHelpByFieldKey[field.Key] = vm.Help;
                    if (field.VisibleIf is not null)
                    {
                        _visibleIfByFieldKey[field.Key] = field.VisibleIf;
                    }
                    if (field.VisibleForInputExtensions is not null)
                    {
                        _visibleForInputExtensionsByFieldKey[field.Key] = field.VisibleForInputExtensions;
                    }

                    if (field.VisibleForTargetFormats is not null)
                    {
                        _visibleForTargetFormatsByFieldKey[field.Key] = field.VisibleForTargetFormats;
                    }

                    ConfigFields.Add(vm);
                }
            }
        }

        if (schema?.FieldBoolRelations is { } rels)
        {
            _fieldBoolRelations.AddRange(rels);
        }

        // 与 RefreshTargetFormatsByVisibility 使用同一套过滤规则，避免先填充 manifest 全量项再过滤时
        // 用户能选到当前输入下无效的格式，随后重建列表覆盖选中项导致 ComboBox 折叠态空白。
        _isReevaluatingUiRules = true;
        try
        {
            RefreshTargetFormatsByVisibility();
        }
        finally
        {
            _isReevaluatingUiRules = false;
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

        } // else HasActivePlugin

        // 全局命名模板与自定义标签：与插件无关，必须在无插件时也能从 config 恢复并写回。
        _loadingUserSettings = true;
        try
        {
            RestoreGlobalUiFromSettings();
        }
        finally
        {
            _loadingUserSettings = false;
        }

        // Evaluate visibleIf right after loading persisted values.
        // Relations are applied only on user-driven driving-checkbox change.
        ReevaluateUiRules(changedFieldKey: null);

        AttachConfigFieldHandlers();
    }

    private void OnVmPersistablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 不再需要实时保存，在应用关闭时统一保存
        // 这里可以添加一些即时反馈，比如状态栏提示
    }

    private void ScheduleSaveUserSettings()
    {
        // 不再需要定时器，在应用关闭时统一保存
        // 这里可以添加一些即时反馈，比如状态栏提示
    }

    public void SaveUserSettingsNow() => SaveSettings();

    public void SaveSettings()
    {
        try
        {
            // 从 UI 更新用户设置
            UpdateUserSettingsFromUI();

            // 更新设置字典
            SettingManager.SetValue(_settings, "Locale", _userSettings.Locale);
            SettingManager.SetValue(_settings, "OutputDir", _userSettings.OutputDir);
            SettingManager.SetValue(_settings, "UseInputDirAsOutput", _userSettings.UseInputDirAsOutput);
            SettingManager.SetValue(_settings, "NamingTemplate", _userSettings.NamingTemplate);
            SettingManager.SetValue(_settings, "CustomToken1Text", _userSettings.CustomToken1Text);
            SettingManager.SetValue(_settings, "CustomToken2Text", _userSettings.CustomToken2Text);
            SettingManager.SetValue(_settings, "CustomToken3Text", _userSettings.CustomToken3Text);
            SettingManager.SetValue(_settings, "EnableParallelProcessing", _userSettings.EnableParallelProcessing);
            SettingManager.SetValue(_settings, "Parallelism", _userSettings.Parallelism);
            SettingManager.SetValue(_settings, "KeepTemp", _userSettings.KeepTemp);
            SettingManager.SetValue(_settings, "EnableContextMenu", _userSettings.EnableContextMenu);
            SettingManager.SetValue(_settings, "AllowedSourceExtensions", _userSettings.AllowedSourceExtensions);
            SettingManager.SetValue(_settings, "AllowedTargetFormats", _userSettings.AllowedTargetFormats);
            SettingManager.SetValue(_settings, "Plugins", _userSettings.Plugins);

            SettingManager.SaveHostSettings(_settings);
            
            // 保存所有插件配置
            SettingManager.SaveAllPluginSettings(_catalog, _pluginSettings);
        }
        catch
        {
            // best effort
        }
    }

    private void UpdateContextMenu()
    {
        try
        {
            Host.Services.ContextMenuManager.UpdateContextMenu(
                _enableContextMenu, 
                _allowedSourceExtensions, 
                _catalog);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating context menu: {ex.Message}");
        }
    }

    private void UpdateUserSettingsFromUI()
    {
        // 从 UI 更新用户设置
        _userSettings.Locale = _hostI18n.Locale;
        _userSettings.InputPaths = null; // 不保存输入路径
        _userSettings.OutputDir = _outputDir;
        _userSettings.UseInputDirAsOutput = _useInputDirAsOutput;
        _userSettings.NamingTemplate = _namingTemplateViewModel.NamingTemplate;

        var (custom1, custom2, custom3) = _namingTemplateViewModel.GetCustomTokenTexts();
        _userSettings.CustomToken1Text = custom1;
        _userSettings.CustomToken2Text = custom2;
        _userSettings.CustomToken3Text = custom3;

        _userSettings.EnableParallelProcessing = _enableParallelProcessing;
        _userSettings.Parallelism = _parallelism;
        _userSettings.KeepTemp = _keepTemp;

        if (_activePlugin?.Manifest?.PluginId is { } pid)
            PersistCurrentPluginUiToStores(pid);
    }

    private PluginEntry? FindPluginEntry(string pluginId) =>
        _catalog.Plugins.FirstOrDefault(e =>
            string.Equals(e.Manifest.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, SettingItem> LoadPluginSettingsDictForPluginId(string pluginId)
    {
        var entry = FindPluginEntry(pluginId);
        return entry is not null
            ? SettingManager.LoadPluginSettingsFromPluginDirectory(entry.PluginDir)
            : SettingManager.LoadPluginSettings(pluginId);
    }

    /// <summary>
    /// 将当前 <see cref="ConfigFields"/> / <see cref="SelectedTargetFormat"/> 写入 <see cref="_pluginSettings"/> 与 <see cref="_userSettings.Plugins"/>，
    /// 与即将写盘的 <c>config.json</c> 一致。切换插件或关闭窗口前必须调用，否则会丢失未写盘的 UI 修改。
    /// </summary>
    private void PersistCurrentPluginUiToStores(string pid)
    {
        _userSettings.Plugins ??= new Dictionary<string, PluginUserSettings>(StringComparer.OrdinalIgnoreCase);
        if (!_userSettings.Plugins.TryGetValue(pid, out var plug))
        {
            plug = new PluginUserSettings();
            _userSettings.Plugins[pid] = plug;
        }

        if (!_pluginSettings.TryGetValue(pid, out var pluginConfig))
        {
            pluginConfig = LoadPluginSettingsDictForPluginId(pid);
            _pluginSettings[pid] = pluginConfig;
        }

        SettingManager.SetValue(pluginConfig, "TargetFormatId", SelectedTargetFormat?.Id);

        foreach (var f in ConfigFields)
        {
            var v = f.GetValue();
            var value = v switch
            {
                null => "",
                bool b => b ? "true" : "false",
                _ => v.ToString() ?? ""
            };
            SettingManager.SetValue(pluginConfig, f.Key, value);
            plug.Fields[f.Key] = value;
        }

        ApplyFieldPersistOverridesToSavedPluginFields(pluginConfig, plug);
    }

    private void RestorePluginUiFromSettings()
    {
        if (_activePlugin?.Manifest?.PluginId is not { } pid)
        {
            return;
        }

        // 确保插件配置已加载
        if (!_pluginSettings.TryGetValue(pid, out var pluginConfig))
        {
            var settings = LoadPluginSettingsDictForPluginId(pid);
            _pluginSettings[pid] = settings;
            pluginConfig = settings;
        }

        // 从插件配置文件读取目标格式
        var targetFormatId = SettingManager.GetValue<string>(pluginConfig, "TargetFormatId");
        if (!string.IsNullOrWhiteSpace(targetFormatId))
        {
            var tf = TargetFormats.FirstOrDefault(t =>
                string.Equals(t.Id, targetFormatId, StringComparison.OrdinalIgnoreCase));
            if (tf is not null)
            {
                SelectedTargetFormat = tf;
            }
        }

        // 从插件配置文件读取各字段值（须用 GetPluginFieldPersistedString：显式空串 "" 也要恢复，不能用 GetValue<string>）
        foreach (var field in ConfigFields)
        {
            var value = SettingManager.GetPluginFieldPersistedString(pluginConfig, field.Key);
            if (value is not null)
                ApplyFieldString(field, value);
        }

        ApplyFieldPersistOverridesAfterRestore();
    }

    private void ApplyCustomTokensFromUserSettingsToUi()
    {
        var c1 = _userSettings.CustomToken1Text ?? "";
        var c2 = _userSettings.CustomToken2Text ?? "";
        var c3 = _userSettings.CustomToken3Text ?? "";
        _customToken1Text = c1;
        _customToken2Text = c2;
        _customToken3Text = c3;
        _namingTemplateViewModel.SetCustomTokenTexts(c1, c2, c3);
        RaisePropertyChanged(nameof(CustomToken1Text));
        RaisePropertyChanged(nameof(CustomToken2Text));
        RaisePropertyChanged(nameof(CustomToken3Text));
        RaiseCustomTokenChipCandidateTextsChanged();
    }

    private void RestoreGlobalUiFromSettings()
    {
        // 与插件无关的全局项：插件列表变化时会再次调用本方法，需与 _userSettings 保持一致
        OutputDir = ExpandOutputPath(_userSettings.OutputDir ?? "");
        UseInputDirAsOutput = _userSettings.UseInputDirAsOutput ?? true;

        ApplyCustomTokensFromUserSettingsToUi();

        if (_userSettings.NamingTemplate is not null)
        {
            _namingTemplateViewModel.NamingTemplate = _userSettings.NamingTemplate.Trim();
        }
    }

    /// <summary>
    /// When manifest <c>configSchema.fieldPersistOverrides</c> rules match current UI, overwrite keys in the
    /// snapshot written to disk so the next launch restores folded defaults, without changing in-session VMs here.
    /// 同时写入 <paramref name="pluginConfig"/>（<c>plugins/&lt;pluginId&gt;/config.json</c>）与 Host 根 <c>config.json</c> 中的 <c>Plugins</c> 快照（<paramref name="plug"/>）。
    /// </summary>
    private void ApplyFieldPersistOverridesToSavedPluginFields(Dictionary<string, SettingItem> pluginConfig, PluginUserSettings plug)
    {
        var schema = _activePlugin?.Manifest?.ConfigSchema;
        if (schema?.FieldPersistOverrides is not { Length: > 0 } rules)
        {
            return;
        }

        foreach (var rule in rules)
        {
            if (rule.When is null || rule.Fields is null || rule.Fields.Count == 0)
            {
                continue;
            }

            if (!TryGetCheckboxValue(rule.When.FieldKey, out var cur) || cur != rule.When.Expected)
            {
                continue;
            }

            foreach (var kv in rule.Fields)
            {
                var s = JsonElementToPersistString(kv.Value);
                plug.Fields[kv.Key] = s;
                SettingManager.SetValue(pluginConfig, kv.Key, s);
            }
        }
    }

    /// <summary>
    /// After loading user-settings into VMs, apply the same rules so legacy files and coerced snapshots
    /// match the intended next-launch UI (e.g. enable off when retain is off).
    /// </summary>
    private void ApplyFieldPersistOverridesAfterRestore()
    {
        var schema = _activePlugin?.Manifest?.ConfigSchema;
        if (schema?.FieldPersistOverrides is not { Length: > 0 } rules)
        {
            return;
        }

        foreach (var rule in rules)
        {
            if (rule.When is null || rule.Fields is null || rule.Fields.Count == 0)
            {
                continue;
            }

            if (!TryGetCheckboxValue(rule.When.FieldKey, out var cur) || cur != rule.When.Expected)
            {
                continue;
            }

            foreach (var kv in rule.Fields)
            {
                var field = ConfigFields.FirstOrDefault(f =>
                    string.Equals(f.Key, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (field is null)
                {
                    continue;
                }

                ApplyFieldString(field, JsonElementToPersistString(kv.Value));
            }
        }
    }

    private static string JsonElementToPersistString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.ToString(),
            _ => el.ToString()
        };
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
            var fieldKey = f.Key;
            PropertyChangedEventHandler h = (_, _) =>
            {
                if (_loadingUserSettings)
                {
                    return;
                }

                if (_isReevaluatingUiRules)
                {
                    return;
                }

                ReevaluateUiRules(fieldKey);
                ScheduleSaveUserSettings();
            };
            f.PropertyChanged += h;
            _configFieldHandlers.Add((f, h));
        }
    }

    private bool TryGetCheckboxValue(string key, out bool value)
    {
        var field = ConfigFields.FirstOrDefault(f =>
            string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        if (field is CheckboxFieldVm c)
        {
            value = c.IsChecked;
            return true;
        }

        value = false;
        return false;
    }

    private string GetTargetSizeUnitId()
    {
        const string targetSizeUnitKey = "targetSizeUnit";
        var field = ConfigFields.FirstOrDefault(f => string.Equals(f.Key, targetSizeUnitKey, StringComparison.OrdinalIgnoreCase));
        if (field is SelectFieldVm s && s.Selected is { } opt)
        {
            return opt.Id;
        }

        return "KB";
    }

    private void ApplyTargetSizeUnitConversionIfNeeded()
    {
        const string targetMinKey = "targetSizeMinKb";
        const string targetMaxKey = "targetSizeMaxKb";

        var newUnitId = GetTargetSizeUnitId();
        if (string.Equals(_lastTargetSizeUnitId, newUnitId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var minField = ConfigFields.OfType<NumberFieldVm>().FirstOrDefault(f =>
            string.Equals(f.Key, targetMinKey, StringComparison.OrdinalIgnoreCase));
        var maxField = ConfigFields.OfType<NumberFieldVm>().FirstOrDefault(f =>
            string.Equals(f.Key, targetMaxKey, StringComparison.OrdinalIgnoreCase));

        if (minField is null || maxField is null)
        {
            _lastTargetSizeUnitId = newUnitId;
            return;
        }

        if (!_isConvertingTargetSizeUnit)
        {
            _isConvertingTargetSizeUnit = true;
            try
            {
                if (string.Equals(_lastTargetSizeUnitId, "KB", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(newUnitId, "MB", StringComparison.OrdinalIgnoreCase))
                {
                    minField.Value /= 1024m;
                    maxField.Value /= 1024m;
                }
                else if (string.Equals(_lastTargetSizeUnitId, "MB", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(newUnitId, "KB", StringComparison.OrdinalIgnoreCase))
                {
                    minField.Value *= 1024m;
                    maxField.Value *= 1024m;
                }
                else
                {
                    // Unknown transition; just update baseline without conversion.
                }
            }
            finally
            {
                _isConvertingTargetSizeUnit = false;
            }
        }

        _lastTargetSizeUnitId = newUnitId;
    }

    private void UpdateTargetSizeLabels()
    {
        var unitId = GetTargetSizeUnitId();
        var zh = _hostI18n.Locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        const string targetMinKey = "targetSizeMinKb";
        const string targetMaxKey = "targetSizeMaxKb";

        if (_baseLabelByFieldKey.TryGetValue(targetMinKey, out var baseMin))
        {
            var suffix = zh ? $"（{unitId}）" : $" ({unitId})";
            var field = ConfigFields.FirstOrDefault(f => string.Equals(f.Key, targetMinKey, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                field.Label = baseMin + suffix;
            }
        }

        if (_baseLabelByFieldKey.TryGetValue(targetMaxKey, out var baseMax))
        {
            var suffix = zh ? $"（{unitId}）" : $" ({unitId})";
            var field = ConfigFields.FirstOrDefault(f => string.Equals(f.Key, targetMaxKey, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                field.Label = baseMax + suffix;
            }
        }
    }

    private void ReevaluateUiRules(string? changedFieldKey)
    {
        if (_isReevaluatingUiRules)
        {
            return;
        }

        _isReevaluatingUiRules = true;
        try
        {
            // 1) apply dependency relations (checkbox -> checkbox), non-save only
            foreach (var rel in _fieldBoolRelations)
            {
                if (rel.If is null || rel.Then is null)
                {
                    continue;
                }

                if (string.Equals(rel.ApplyWhen, "save", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(rel.If.FieldKey, changedFieldKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryGetCheckboxValue(rel.If.FieldKey, out var curValue) &&
                    curValue == rel.If.Expected)
                {
                    var targetField = ConfigFields.FirstOrDefault(f =>
                        string.Equals(f.Key, rel.Then.TargetKey, StringComparison.OrdinalIgnoreCase));
                    if (targetField is CheckboxFieldVm cb &&
                        cb.IsChecked != rel.Then.Value)
                    {
                        cb.IsChecked = rel.Then.Value;
                    }
                }
            }

            // 2) filter target formats first so SelectedTargetFormat is stable for visibleForTargetFormats
            RefreshTargetFormatsByVisibility();

            // 3) show/hide fields via visibleIf, visibleForInputExtensions, visibleForTargetFormats
            var inputExt = InputFiles.FirstOrDefault()?.FullPath is { } p
                ? Path.GetExtension(p).TrimStart('.').ToLowerInvariant()
                : null;

            var selectedTargetId = SelectedTargetFormat?.Id;

            foreach (var field in ConfigFields)
            {
                var show = true;

                if (_visibleIfByFieldKey.TryGetValue(field.Key, out var vIf) && vIf is not null)
                {
                    show = false;
                    if (TryGetCheckboxValue(vIf.FieldKey, out var controllingValue))
                    {
                        show = controllingValue == vIf.Expected;
                    }
                }

                if (show && _visibleForInputExtensionsByFieldKey.TryGetValue(field.Key, out var exts) && exts is { Length: > 0 })
                {
                    if (string.IsNullOrEmpty(inputExt) ||
                        !exts.Any(e => string.Equals(e, inputExt, StringComparison.OrdinalIgnoreCase)))
                    {
                        show = false;
                    }
                }

                if (show && _visibleForTargetFormatsByFieldKey.TryGetValue(field.Key, out var tfIds) && tfIds is { Length: > 0 })
                {
                    if (string.IsNullOrWhiteSpace(selectedTargetId) ||
                        !tfIds.Any(t => string.Equals(t, selectedTargetId, StringComparison.OrdinalIgnoreCase)))
                    {
                        show = false;
                    }
                }

                field.IsVisible = show;
            }

            // 4) target-size unit conversion + dynamic labels
            ApplyTargetSizeUnitConversionIfNeeded();
            UpdateTargetSizeLabels();
        }
        finally
        {
            _isReevaluatingUiRules = false;
        }
    }

    private void RefreshTargetFormatsByVisibility()
    {
        var candidates = new List<TargetFormatVm>();
        var prevSelectedId = SelectedTargetFormat?.Id;

        var inputExt = InputFiles.FirstOrDefault()?.FullPath is { } p
            ? Path.GetExtension(p).TrimStart('.').ToLowerInvariant()
            : null;

        foreach (var tf in _allTargetFormatModels)
        {
            var visible = true;

            if (tf.InputExtensions is { Length: > 0 })
            {
                if (string.IsNullOrEmpty(inputExt) ||
                    !tf.InputExtensions.Any(e => string.Equals(e, inputExt, StringComparison.OrdinalIgnoreCase)))
                {
                    visible = false;
                }
            }

            if (visible && tf.VisibleIf is not null)
            {
                if (TryGetCheckboxValue(tf.VisibleIf.FieldKey, out var controllingValue))
                {
                    visible = controllingValue == tf.VisibleIf.Expected;
                }
                else
                {
                    visible = false;
                }
            }

            if (visible)
            {
                var entry = _activePlugin;
                if (entry is null)
                {
                    continue;
                }

                candidates.Add(new TargetFormatVm(tf.Id, _pluginI18n.T(entry, tf.DisplayNameKey, _hostI18n.Locale)));
            }
        }

        TargetFormats.Clear();
        foreach (var c in candidates)
        {
            TargetFormats.Add(c);
        }

        if (!TargetFormats.Any())
        {
            SelectedTargetFormat = null;
            return;
        }

        TargetFormatVm? next = null;
        if (!string.IsNullOrWhiteSpace(prevSelectedId))
        {
            next = TargetFormats.FirstOrDefault(t =>
                string.Equals(t.Id, prevSelectedId, StringComparison.OrdinalIgnoreCase));
        }

        SelectedTargetFormat = next ?? TargetFormats.First();
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

    private static IEnumerable<string> GetAllSupportedExtensions(PluginCatalog catalog)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in catalog.Plugins)
        {
            if (plugin.Manifest.SupportedInputExtensions is { } exts)
            {
                foreach (var ext in exts)
                {
                    var normalized = ext.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        extensions.Add(normalized);
                    }
                }
            }
        }
        return extensions;
    }

    private PluginEntry? ResolveActivePlugin()
    {
        var firstInput = InputFiles.FirstOrDefault()?.FullPath;
        if (string.IsNullOrWhiteSpace(firstInput))
        {
            return null;
        }

        return PluginRouter.RouteByInputPath(_catalog, firstInput);
    }

    private bool HasAnyInputPaths() => InputFiles.Count > 0;

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

            var inputPaths = InputFiles.Select(f => f.FullPath).ToArray();

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

            // 本轮任务已跑完且未取消时，清空输入列表，便于继续下一批。
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() => InputFiles.Clear());
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
            var namingNow = DateTime.Now;

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
                            ["ext"] = targetFormatId,
                            ["timeYmd"] = namingNow.ToString("yyyy-MM-dd"),
                            ["timeHms"] = namingNow.ToString("HH-mm-ss")
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
                    var namingNow = DateTime.Now;

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
                                    ["ext"] = targetFormatId,
                                    ["timeYmd"] = namingNow.ToString("yyyy-MM-dd"),
                                    ["timeHms"] = namingNow.ToString("HH-mm-ss")
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
        AppServices.ReloadPlugins(AppContext.BaseDirectory);
        _catalog = AppServices.Plugins;
        HasPlugins = _catalog.Plugins.Count > 0;
        UpdateConfigPlaceholder();

        var allSupportedExtensions = GetAllSupportedExtensions(_catalog);
        _inputFileViewModel.UpdateSupportedExtensions(allSupportedExtensions);

        // If context menu integration is enabled, ensure it reflects the latest plugin set immediately.
        if (EnableContextMenu)
        {
            UpdateContextMenu();
        }

        ReloadPluginContext();
    }

    public void AddInputPaths(IEnumerable<string> paths)
    {
        _inputFileViewModel.AddInputPaths(paths);
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
        var outputDirRaw = UseInputDirAsOutput
            ? (Path.GetDirectoryName(inputPath) ?? "")
            : (OutputDir ?? "").Trim();
        var outputDir = string.IsNullOrWhiteSpace(outputDirRaw)
            ? Path.Combine(AppContext.BaseDirectory, "output")
            : ExpandOutputPath(outputDirRaw);
        Directory.CreateDirectory(outputDir);

        var template = (NamingTemplate ?? "").Trim();
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "{base}";
        }

        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var now = DateTime.Now;
        var (custom1, custom2, custom3) = _namingTemplateViewModel.GetCustomTokenTexts();
        var fileName = template
            .Replace("{base}", baseName, StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", targetExt, StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", index.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{timeYmd}", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{timeHms}", now.ToString("HH-mm-ss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{custom1}", custom1, StringComparison.OrdinalIgnoreCase)
            .Replace("{custom2}", custom2, StringComparison.OrdinalIgnoreCase)
            .Replace("{custom3}", custom3, StringComparison.OrdinalIgnoreCase);

        fileName = fileName.TrimEnd('.');
        var expectedSuffix = "." + targetExt;
        if (!fileName.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"{fileName}{expectedSuffix}";
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

    private static bool TryGetBoolDefault(JsonElement? el, bool fallback)
    {
        if (el is null)
        {
            return fallback;
        }

        try
        {
            return el.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(el.Value.GetString(), out var b) ? b : fallback,
                JsonValueKind.Number => el.Value.GetInt32() != 0,
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
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

    /// <summary>展开 OutputDir 中的 %USERPROFILE% 等；在无法展开时（如 Linux 无 USERPROFILE）回退到「文档/convertool/output」。</summary>
    private static string ExpandOutputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var t = path.Trim();
        var expanded = Environment.ExpandEnvironmentVariables(t);
        if (expanded.Contains('%') && t.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "convertool", "output");
        return expanded;
    }
}

/// <summary>
/// 与 manifest <c>supportedTargetFormats</c> 中一项对应：<see cref="Id"/> 为可转换格式 id；
/// <see cref="Text"/> 为 <c>displayNameKey</c> 在当前语言下的展示文案。
/// </summary>
public sealed class TargetFormatVm
{
    public string Id { get; }
    public string Text { get; }

    public TargetFormatVm(string id, string text)
    {
        Id = id;
        Text = text;
    }

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

