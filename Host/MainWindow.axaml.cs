using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Linq;
using Host.ViewModels;

namespace Host;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainWindowViewModel(AppServices.Plugins, AppServices.PluginI18n, AppServices.I18n);
        DataContext = _vm;

        Opened += OnOpened;
        Closing += (_, _) => _vm?.SaveUserSettingsNow();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        _vm.TopLevel = TopLevel.GetTopLevel(this);
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainWindowViewModel.ProcessLog), StringComparison.Ordinal))
        {
            return;
        }

        if (this.FindControl<TextBox>("ProcessLogBox") is not { } tb)
        {
            return;
        }

        var text = tb.Text ?? "";
        tb.CaretIndex = text.Length;
        tb.SelectionStart = text.Length;
        tb.SelectionEnd = text.Length;
    }

    private void OnInputDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnInputDrop(object? sender, DragEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var items = e.DataTransfer.TryGetFiles();
        if (items is null)
        {
            return;
        }

        var paths = items
            .Select(i => i.Path.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        if (paths.Length > 0)
        {
            _vm.AddInputPaths(paths);
        }

        e.Handled = true;
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Clicks on "background" should clear keyboard focus (exit edit modes, remove focus rings).
        // ComboBox "selected item" is state and may remain; this only clears focus/interaction state.
        var pos = e.GetPosition(this);
        var hit = this.InputHitTest(pos);

        if (hit is Visual hitVisual)
        {
            Visual? cur = hitVisual;
            while (cur is not null)
            {
                if (cur is Control c)
                {
                    if (c.Focusable && c.IsEnabled && c.IsVisible)
                    {
                        return; // let the control receive focus as usual
                    }
                }

                cur = cur.Parent as Visual;
            }
        }

        this.FocusManager?.ClearFocus();
    }

    private async void OnOpenPluginManager(object? sender, RoutedEventArgs e)
    {
        var win = new PluginManagerWindow();
        await win.ShowDialog(this);
        _vm?.RefreshPluginsFromDisk();
    }
}