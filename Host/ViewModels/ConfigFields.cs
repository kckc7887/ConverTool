using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Globalization;

namespace Host.ViewModels;

internal sealed class SyncCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public SyncCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute();
            // Notify potential bindings/subscribers even though CanExecute is effectively constant.
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public abstract class ConfigFieldVm : ObservableObject
{
    protected ConfigFieldVm(string key, string label, string? help)
    {
        Key = key;
        _label = label;
        _help = help;
    }

    public string Key { get; }
    private string _label = "";
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    private string? _help;
    public string? Help
    {
        get => _help;
        set => SetProperty(ref _help, value);
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public abstract object? GetValue();
}

public sealed class TextFieldVm : ConfigFieldVm
{
    private string _text = "";

    public TextFieldVm(string key, string label, string? help, string? defaultValue)
        : base(key, label, help)
    {
        _text = defaultValue ?? "";
    }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public override object? GetValue() => Text;
}

public sealed class CheckboxFieldVm : ConfigFieldVm
{
    private bool _isChecked;

    public CheckboxFieldVm(string key, string label, string? help, bool defaultValue)
        : base(key, label, help)
    {
        _isChecked = defaultValue;
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public override object? GetValue() => IsChecked;
}

public sealed class SelectFieldVm : ConfigFieldVm
{
    public SelectFieldVm(string key, string label, string? help, IReadOnlyList<OptionVm> options, string? defaultId)
        : base(key, label, help)
    {
        Options = new ObservableCollection<OptionVm>(options);
        Selected = Options.FirstOrDefault(o => string.Equals(o.Id, defaultId, StringComparison.OrdinalIgnoreCase))
                   ?? Options.FirstOrDefault();
    }

    public ObservableCollection<OptionVm> Options { get; }

    private OptionVm? _selected;
    public OptionVm? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public override object? GetValue() => Selected?.Id;
}

public sealed class PathFieldVm : ConfigFieldVm
{
    private string _path = "";

    public PathFieldVm(string key, string label, string? help, string? defaultValue, string kind)
        : base(key, label, help)
    {
        _path = defaultValue ?? "";
        Kind = kind;
    }

    public string Kind { get; } // "File" | "Folder"

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public override object? GetValue() => Path;
}

public sealed class RangeFieldVm : ConfigFieldVm
{
    private double _value;
    private const double MaxTickCountForUi = 2000;

    public RangeFieldVm(string key, string label, string? help, double min, double max, double step, double defaultValue)
        : base(key, label, help)
    {
        // Be defensive: malformed plugins might provide Min > Max.
        if (min <= max)
        {
            Min = min;
            Max = max;
        }
        else
        {
            Min = max;
            Max = min;
        }

        // Step is only used for UI snapping/ticks; keep it finite and positive.
        Step = step <= 0 || double.IsNaN(step) || double.IsInfinity(step) ? 1 : step;
        _value = ClampToRange(defaultValue);

        var span = Max - Min;
        if (span <= 0 || double.IsNaN(span) || double.IsInfinity(span))
        {
            TickFrequency = 1;
            IsSnapToTickEnabled = false;
            return;
        }

        var tickCount = span / Step;
        if (tickCount <= 0 || double.IsNaN(tickCount) || double.IsInfinity(tickCount))
        {
            TickFrequency = 1;
            IsSnapToTickEnabled = false;
            return;
        }

        // If tick count is too high, Avalonia slider snapping can become extremely slow / unresponsive.
        if (tickCount > MaxTickCountForUi)
        {
            IsSnapToTickEnabled = false;
            TickFrequency = span / MaxTickCountForUi;
            if (TickFrequency <= 0 || double.IsNaN(TickFrequency) || double.IsInfinity(TickFrequency))
            {
                TickFrequency = 1;
            }
        }
        else
        {
            IsSnapToTickEnabled = true;
            TickFrequency = Step;
        }
    }

    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public double TickFrequency { get; }
    public bool IsSnapToTickEnabled { get; }

    public double Value
    {
        get => _value;
        set
        {
            var next = ClampToRange(value);
            if (SetProperty(ref _value, next))
            {
                RaisePropertyChanged(nameof(ValueText));
            }
        }
    }

    // Used by the numeric input box; keep invariant culture so plugin receives stable values.
    public string ValueText
    {
        get => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                Value = parsed;
            }
        }
    }

    private double ClampToRange(double v)
    {
        if (v < Min) v = Min;
        if (v > Max) v = Max;
        return v;
    }

    public override object? GetValue() => ValueText;
}

public sealed class NumberFieldVm : ConfigFieldVm
{
    private decimal _value;

    public NumberFieldVm(string key, string label, string? help, double min, double max, double step, double defaultValue)
        : base(key, label, help)
    {
        if (min <= max)
        {
            Min = ToDecimalSafe(min);
            Max = ToDecimalSafe(max);
        }
        else
        {
            Min = ToDecimalSafe(max);
            Max = ToDecimalSafe(min);
        }

        // Decimal increment for NumericUpDown; keep it finite and positive.
        if (step <= 0 || double.IsNaN(step) || double.IsInfinity(step))
        {
            step = 1;
        }
        Step = ToDecimalSafe(step);
        if (Step <= 0) Step = 1;

        _value = ClampToRange(ToDecimalSafe(defaultValue));

        // Used by the spinbox-like UI: keep the input box width compact
        // based on the "max allowed value" string length.
        MaxTextLength = FormatForLength(Max).Length;
        SuggestedTextBoxWidth = Math.Clamp(MaxTextLength * 8 + 28, 70, 280);

        IncreaseCommand = new SyncCommand(() => Value = Value + Step);
        DecreaseCommand = new SyncCommand(() => Value = Value - Step);
    }

    public decimal Min { get; }
    public decimal Max { get; }
    public decimal Step { get; }

    public int MaxTextLength { get; }
    public double SuggestedTextBoxWidth { get; }

    public decimal Value
    {
        get => _value;
        set
        {
            var next = ClampToRange(value);
            if (SetProperty(ref _value, next))
            {
                RaisePropertyChanged(nameof(ValueText));
            }
        }
    }

    // NumericUpDown binds to decimal, but Host/Plugin protocol persists values as strings.
    public string ValueText
    {
        get => Value.ToString(CultureInfo.InvariantCulture);
        set
        {
            var s = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                return;
            }

            // Accept both "." and "," decimal separators for better UX in zh-CN.
            s = s.Replace(',', '.');
            if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                Value = parsed;
            }
        }
    }

    public ICommand IncreaseCommand { get; }
    public ICommand DecreaseCommand { get; }

    public override object? GetValue() => ValueText;

    private static string FormatForLength(decimal v)
        => v.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

    private static decimal ToDecimalSafe(double v)
    {
        try
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return 0;
            }

            // Cast may overflow if v is huge; still protect with try/catch.
            return (decimal)v;
        }
        catch
        {
            return 0;
        }
    }

    private decimal ClampToRange(decimal v)
    {
        if (v < Min) v = Min;
        if (v > Max) v = Max;
        return v;
    }
}

public sealed record OptionVm(string Id, string Text)
{
    public override string ToString() => Text;
}

