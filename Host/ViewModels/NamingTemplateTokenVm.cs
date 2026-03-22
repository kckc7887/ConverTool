using System;
using System.Windows.Input;

namespace Host.ViewModels;

public sealed class NamingTemplateTokenVm : ObservableObject
{
    private readonly string _baseDisplayText;
    private readonly string _indexDisplayText;
    private readonly string _timeDateDisplayText;
    private readonly string _timeHmsDisplayText;
    private readonly string _custom1DisplayText;
    private readonly string _custom2DisplayText;
    private readonly string _custom3DisplayText;

    public NamingTemplateTokenVm(
        string value,
        string baseDisplayText,
        string indexDisplayText,
        string timeDateDisplayText,
        string timeHmsDisplayText,
        string custom1DisplayText,
        string custom2DisplayText,
        string custom3DisplayText,
        Action<NamingTemplateTokenVm> onRemove)
    {
        Id = Guid.NewGuid();
        Value = value ?? "";
        RemoveCommand = new SyncCommand(() => onRemove(this));

        _baseDisplayText = baseDisplayText ?? "base";
        _indexDisplayText = indexDisplayText ?? "index";
        _timeDateDisplayText = timeDateDisplayText ?? "{timeYmd}";
        _timeHmsDisplayText = timeHmsDisplayText ?? "{timeHms}";
        _custom1DisplayText = custom1DisplayText ?? "";
        _custom2DisplayText = custom2DisplayText ?? "";
        _custom3DisplayText = custom3DisplayText ?? "";
    }

    public Guid Id { get; }

    public string Value { get; }

    public string DisplayText
        => Value.Equals("{base}", StringComparison.OrdinalIgnoreCase) ? _baseDisplayText
            : Value.Equals("{index}", StringComparison.OrdinalIgnoreCase) ? _indexDisplayText
            : Value.Equals("{timeYmd}", StringComparison.OrdinalIgnoreCase) ? _timeDateDisplayText
            : Value.Equals("{timeHms}", StringComparison.OrdinalIgnoreCase) ? _timeHmsDisplayText
            : Value.Equals("{custom1}", StringComparison.OrdinalIgnoreCase) ? (!string.IsNullOrWhiteSpace(_custom1DisplayText) ? _custom1DisplayText : Value)
            : Value.Equals("{custom2}", StringComparison.OrdinalIgnoreCase) ? (!string.IsNullOrWhiteSpace(_custom2DisplayText) ? _custom2DisplayText : Value)
            : Value.Equals("{custom3}", StringComparison.OrdinalIgnoreCase) ? (!string.IsNullOrWhiteSpace(_custom3DisplayText) ? _custom3DisplayText : Value)
            : Value;

    public ICommand RemoveCommand { get; }
}
