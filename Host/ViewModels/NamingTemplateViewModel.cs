using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace Host.ViewModels;

public sealed class NamingTemplateViewModel : ObservableObject
{
    private static readonly HashSet<string> ValidTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "{base}",
        "{index}",
        "{timeYmd}",
        "{timeHms}",
        "{custom1}",
        "{custom2}",
        "{custom3}",
    };

    private string _namingTemplate = "";
    private bool _syncingNamingTemplateTokens;

    private string _namingTemplateTokenBaseText = "base";
    public string NamingTemplateTokenBaseText => _namingTemplateTokenBaseText;

    private string _namingTemplateTokenIndexText = "index";
    public string NamingTemplateTokenIndexText => _namingTemplateTokenIndexText;

    private string _namingTemplateTokenTimeDateText = "{timeYmd}";
    private string _namingTemplateTokenTimeHmsText = "{timeHms}";

    public ObservableCollection<NamingTemplateTokenVm> NamingTemplateSelectedTags { get; } = new();

    private bool _hasNamingTemplateBaseToken;
    public bool HasNamingTemplateBaseToken
    {
        get => _hasNamingTemplateBaseToken;
        private set => SetProperty(ref _hasNamingTemplateBaseToken, value);
    }

    private bool _hasNamingTemplateIndexToken;
    public bool HasNamingTemplateIndexToken
    {
        get => _hasNamingTemplateIndexToken;
        private set => SetProperty(ref _hasNamingTemplateIndexToken, value);
    }

    private bool _hasNamingTemplateTimeYmdToken;
    public bool HasNamingTemplateTimeYmdToken
    {
        get => _hasNamingTemplateTimeYmdToken;
        private set => SetProperty(ref _hasNamingTemplateTimeYmdToken, value);
    }

    private bool _hasNamingTemplateTimeHmsToken;
    public bool HasNamingTemplateTimeHmsToken
    {
        get => _hasNamingTemplateTimeHmsToken;
        private set => SetProperty(ref _hasNamingTemplateTimeHmsToken, value);
    }

    private bool _hasNamingTemplateCustom1Token;
    public bool HasNamingTemplateCustom1Token
    {
        get => _hasNamingTemplateCustom1Token;
        private set => SetProperty(ref _hasNamingTemplateCustom1Token, value);
    }

    private bool _hasNamingTemplateCustom2Token;
    public bool HasNamingTemplateCustom2Token
    {
        get => _hasNamingTemplateCustom2Token;
        private set => SetProperty(ref _hasNamingTemplateCustom2Token, value);
    }

    private bool _hasNamingTemplateCustom3Token;
    public bool HasNamingTemplateCustom3Token
    {
        get => _hasNamingTemplateCustom3Token;
        private set => SetProperty(ref _hasNamingTemplateCustom3Token, value);
    }

    public string NamingTemplateBaseCandidateText => NamingTemplateTokenBaseText;
    public string NamingTemplateBaseCandidateValue => "{base}";
    public string NamingTemplateIndexCandidateText => NamingTemplateTokenIndexText;
    public string NamingTemplateIndexCandidateValue => "{index}";

    public string NamingTemplateTimeDateCandidateText => _namingTemplateTokenTimeDateText;
    public string NamingTemplateTimeDateCandidateValue => "{timeYmd}";

    public string NamingTemplateTimeHmsCandidateText => _namingTemplateTokenTimeHmsText;
    public string NamingTemplateTimeHmsCandidateValue => "{timeHms}";

    // 自定义标签文本（由用户设置）；空时显示由 Host i18n 提供的占位文案（见 SetCustomTokenDefaultLabels）
    private string _customToken1Text = "";
    private string _customToken2Text = "";
    private string _customToken3Text = "";
    private string _customToken1DefaultLabel = "";
    private string _customToken2DefaultLabel = "";
    private string _customToken3DefaultLabel = "";

    public void SetCustomTokenDefaultLabels(string d1, string d2, string d3)
    {
        _customToken1DefaultLabel = d1 ?? "";
        _customToken2DefaultLabel = d2 ?? "";
        _customToken3DefaultLabel = d3 ?? "";
        RaisePropertyChanged(nameof(NamingTemplateCustom1CandidateText));
        RaisePropertyChanged(nameof(NamingTemplateCustom2CandidateText));
        RaisePropertyChanged(nameof(NamingTemplateCustom3CandidateText));
    }

    public string NamingTemplateCustom1CandidateText =>
        string.IsNullOrWhiteSpace(_customToken1Text)
            ? (string.IsNullOrWhiteSpace(_customToken1DefaultLabel) ? "Custom 1" : _customToken1DefaultLabel)
            : _customToken1Text;
    public string NamingTemplateCustom1CandidateValue => "{custom1}";
    public string NamingTemplateCustom2CandidateText =>
        string.IsNullOrWhiteSpace(_customToken2Text)
            ? (string.IsNullOrWhiteSpace(_customToken2DefaultLabel) ? "Custom 2" : _customToken2DefaultLabel)
            : _customToken2Text;
    public string NamingTemplateCustom2CandidateValue => "{custom2}";
    public string NamingTemplateCustom3CandidateText =>
        string.IsNullOrWhiteSpace(_customToken3Text)
            ? (string.IsNullOrWhiteSpace(_customToken3DefaultLabel) ? "Custom 3" : _customToken3DefaultLabel)
            : _customToken3Text;
    public string NamingTemplateCustom3CandidateValue => "{custom3}";

    public ObservableCollection<string> NamingTemplateActionCandidates { get; } = new()
    {
        "{base}",
        "{index}",
        "{timeYmd}",
        "{timeHms}",
        "{custom1}",
        "{custom2}",
        "{custom3}",
    };

    public string NamingTemplate
    {
        get => _namingTemplate;
        set
        {
            var normalized = NormalizeNamingTemplate(value);
            if (SetProperty(ref _namingTemplate, normalized))
            {
                if (!_syncingNamingTemplateTokens)
                {
                    InitNamingTemplateTokensFromTemplate(_namingTemplate);
                }
            }
        }
    }

    public event EventHandler? NamingTemplateChanged;

    public NamingTemplateViewModel()
    {
    }

    public void Initialize(string template)
    {
        InitNamingTemplateTokensFromTemplate(template);
    }

    public void SetTokenTexts(
        string tokenBaseText,
        string tokenIndexText,
        string tokenTimeDateText,
        string tokenTimeHmsText)
    {
        _namingTemplateTokenBaseText = tokenBaseText;
        _namingTemplateTokenIndexText = tokenIndexText;
        _namingTemplateTokenTimeDateText = tokenTimeDateText;
        _namingTemplateTokenTimeHmsText = tokenTimeHmsText;

        RaisePropertyChanged(nameof(NamingTemplateTokenBaseText));
        RaisePropertyChanged(nameof(NamingTemplateTokenIndexText));
        RaisePropertyChanged(nameof(NamingTemplateTimeDateCandidateText));
        RaisePropertyChanged(nameof(NamingTemplateTimeHmsCandidateText));
        RaisePropertyChanged(nameof(NamingTemplateBaseCandidateText));
        RaisePropertyChanged(nameof(NamingTemplateIndexCandidateText));

        InitNamingTemplateTokensFromTemplate(NamingTemplate);
    }

    public void SetCustomTokenTexts(string? custom1, string? custom2, string? custom3)
    {
        _customToken1Text = custom1 ?? "";
        _customToken2Text = custom2 ?? "";
        _customToken3Text = custom3 ?? "";

        RaisePropertyChanged(nameof(NamingTemplateCustom1CandidateText));
        RaisePropertyChanged(nameof(NamingTemplateCustom2CandidateText));
        RaisePropertyChanged(nameof(NamingTemplateCustom3CandidateText));
        RaisePropertyChanged(nameof(NamingTemplateCustom1CandidateValue));
        RaisePropertyChanged(nameof(NamingTemplateCustom2CandidateValue));
        RaisePropertyChanged(nameof(NamingTemplateCustom3CandidateValue));

        // 重新初始化以更新显示文本
        InitNamingTemplateTokensFromTemplate(NamingTemplate);

        // 触发命名模板变更事件，以便保存用户设置
        NamingTemplateChanged?.Invoke(this, EventArgs.Empty);
    }

    public (string Custom1, string Custom2, string Custom3) GetCustomTokenTexts()
    {
        return (_customToken1Text, _customToken2Text, _customToken3Text);
    }

    private static string NormalizeNamingTemplate(string? template)
    {
        var t = (template ?? "").Trim();

        t = Regex.Replace(t, @"\.\{ext\}", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\{ext\}", "", RegexOptions.IgnoreCase);

        return t.Trim();
    }

    private void InitNamingTemplateTokensFromTemplate(string template)
    {
        _syncingNamingTemplateTokens = true;
        try
        {
            NamingTemplateSelectedTags.Clear();

            var t = (template ?? "").Trim();

            var parts = t.Split('_', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                // 只添加有效的标签
                if (!ValidTokens.Contains(part))
                {
                    continue;
                }

                NamingTemplateSelectedTags.Add(new NamingTemplateTokenVm(
                    part,
                    NamingTemplateTokenBaseText,
                    NamingTemplateTokenIndexText,
                    _namingTemplateTokenTimeDateText,
                    _namingTemplateTokenTimeHmsText,
                    _customToken1Text,
                    _customToken2Text,
                    _customToken3Text,
                    RemoveNamingTemplateToken
                ));
            }
        }
        finally
        {
            _syncingNamingTemplateTokens = false;
        }

        RecomputeNamingTemplateFromSelectedTags();
    }

    private void RecomputeNamingTemplateFromSelectedTags()
    {
        if (_syncingNamingTemplateTokens)
        {
            return;
        }

        var composed = string.Join("_", NamingTemplateSelectedTags.Select(t => t.Value).Where(v => v is not null));

        NamingTemplate = composed;

        UpdateActionCandidateStates();

        NamingTemplateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateActionCandidateStates()
    {
        var hasBase = false;
        var hasIndex = false;
        var hasTimeYmd = false;
        var hasTimeHms = false;
        var hasCustom1 = false;
        var hasCustom2 = false;
        var hasCustom3 = false;

        foreach (var tag in NamingTemplateSelectedTags)
        {
            if (string.Equals(tag.Value, "{base}", StringComparison.OrdinalIgnoreCase))
                hasBase = true;
            else if (string.Equals(tag.Value, "{index}", StringComparison.OrdinalIgnoreCase))
                hasIndex = true;
            else if (string.Equals(tag.Value, "{timeYmd}", StringComparison.OrdinalIgnoreCase))
                hasTimeYmd = true;
            else if (string.Equals(tag.Value, "{timeHms}", StringComparison.OrdinalIgnoreCase))
                hasTimeHms = true;
            else if (string.Equals(tag.Value, "{custom1}", StringComparison.OrdinalIgnoreCase))
                hasCustom1 = true;
            else if (string.Equals(tag.Value, "{custom2}", StringComparison.OrdinalIgnoreCase))
                hasCustom2 = true;
            else if (string.Equals(tag.Value, "{custom3}", StringComparison.OrdinalIgnoreCase))
                hasCustom3 = true;
        }

        HasNamingTemplateBaseToken = hasBase;
        HasNamingTemplateIndexToken = hasIndex;
        HasNamingTemplateTimeYmdToken = hasTimeYmd;
        HasNamingTemplateTimeHmsToken = hasTimeHms;
        HasNamingTemplateCustom1Token = hasCustom1;
        HasNamingTemplateCustom2Token = hasCustom2;
        HasNamingTemplateCustom3Token = hasCustom3;
    }

    public void ToggleNamingTemplateCandidate(string? value)
    {
        var v = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v))
        {
            return;
        }

        var existing = NamingTemplateSelectedTags.FirstOrDefault(t =>
            string.Equals(t.Value, v, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            RemoveNamingTemplateToken(existing);
        }
        else
        {
            AddNamingTemplateToken(v);
        }
    }

    private void RemoveNamingTemplateToken(NamingTemplateTokenVm token)
    {
        if (token is null)
        {
            return;
        }

        NamingTemplateSelectedTags.Remove(token);
        RecomputeNamingTemplateFromSelectedTags();
    }

    public void AddNamingTemplateToken(string value)
    {
        var v = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v))
        {
            return;
        }

        NamingTemplateSelectedTags.Add(new NamingTemplateTokenVm(
            v,
            NamingTemplateTokenBaseText,
            NamingTemplateTokenIndexText,
            _namingTemplateTokenTimeDateText,
            _namingTemplateTokenTimeHmsText,
            _customToken1Text,
            _customToken2Text,
            _customToken3Text,
            RemoveNamingTemplateToken
        ));
        RecomputeNamingTemplateFromSelectedTags();
    }

    public void MoveNamingTemplateToken(NamingTemplateTokenVm from, NamingTemplateTokenVm to)
    {
        if (from is null || to is null || ReferenceEquals(from, to))
        {
            return;
        }

        var fromIndex = NamingTemplateSelectedTags.IndexOf(from);
        var toIndex = NamingTemplateSelectedTags.IndexOf(to);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
        {
            return;
        }

        NamingTemplateSelectedTags.Move(fromIndex, toIndex);
        RecomputeNamingTemplateFromSelectedTags();
    }
}
