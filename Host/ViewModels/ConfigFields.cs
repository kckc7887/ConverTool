using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Host.ViewModels;

public abstract class ConfigFieldVm : ObservableObject
{
    protected ConfigFieldVm(string key, string label, string? help)
    {
        Key = key;
        Label = label;
        Help = help;
    }

    public string Key { get; }
    public string Label { get; }
    public string? Help { get; }

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

    public RangeFieldVm(string key, string label, string? help, double min, double max, double step, double defaultValue)
        : base(key, label, help)
    {
        Min = min;
        Max = max;
        Step = step <= 0 ? 1 : step;
        _value = ClampToRange(defaultValue);
    }

    public double Min { get; }
    public double Max { get; }
    public double Step { get; }

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

public sealed record OptionVm(string Id, string Text)
{
    public override string ToString() => Text;
}

