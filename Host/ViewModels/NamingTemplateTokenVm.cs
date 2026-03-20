using System;
using System.Windows.Input;

namespace Host.ViewModels;

public sealed class NamingTemplateTokenVm : ObservableObject
{
    private readonly string _baseDisplayText;
    private readonly string _indexDisplayText;
    private readonly string _timeDateDisplayText;
    private readonly string _timeHmsDisplayText;

    public NamingTemplateTokenVm(
        string value,
        string baseDisplayText,
        string indexDisplayText,
        string timeDateDisplayText,
        string timeHmsDisplayText,
        Action<NamingTemplateTokenVm> onRemove)
    {
        Id = Guid.NewGuid();
        Value = value ?? "";
        RemoveCommand = new SyncCommand(() => onRemove(this));

        _baseDisplayText = baseDisplayText ?? "base";
        _indexDisplayText = indexDisplayText ?? "index";
        _timeDateDisplayText = timeDateDisplayText ?? "{timeYmd}";
        _timeHmsDisplayText = timeHmsDisplayText ?? "{timeHms}";
    }

    public Guid Id { get; }

    public string Value { get; }

    public string DisplayText
        => Value.Equals("{base}", StringComparison.OrdinalIgnoreCase) ? _baseDisplayText
            : Value.Equals("{index}", StringComparison.OrdinalIgnoreCase) ? _indexDisplayText
            : Value.Equals("{timeYmd}", StringComparison.OrdinalIgnoreCase) ? _timeDateDisplayText
            : Value.Equals("{timeHms}", StringComparison.OrdinalIgnoreCase) ? _timeHmsDisplayText
            : Value;

    public ICommand RemoveCommand { get; }
}
