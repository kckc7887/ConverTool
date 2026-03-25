using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Host.ViewModels;

namespace Host;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private NamingTemplateTokenVm? _draggingToken;
    private Border? _draggingTokenChip;
    private Border? _hoveredTokenChip;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainWindowViewModel(AppServices.Plugins, AppServices.PluginI18n, AppServices.I18n);
        DataContext = _vm;

        Opened += (_, _) => OnOpened();
        Closing += (_, _) => _vm?.SaveUserSettingsNow();
    }

    private void OnOpened()
    {
        if (_vm is null)
        {
            return;
        }

        _vm.TopLevel = TopLevel.GetTopLevel(this);
        _vm.ShowErrorDialogAsync = ShowErrorDialogAsync;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }
    
    public void AddInputPaths(string[] paths)
    {
        if (_vm is null)
        {
            return;
        }
        
        _vm.AddInputPaths(paths);
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

    private void OnTargetFormatFlyoutListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Space)
        {
            return;
        }

        if (this.FindControl<Button>("TargetFormatChooserButton") is not { Flyout: { } flyout })
        {
            return;
        }

        flyout.Hide();
    }

    private void OnTargetFormatFlyoutListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ListBox)
        {
            return;
        }

        if (e.Source is not Interactive interactive)
        {
            return;
        }

        if (FindAncestor<ListBoxItem>(interactive) is null)
        {
            return;
        }

        if (this.FindControl<Button>("TargetFormatChooserButton") is not { Flyout: { } flyout })
        {
            return;
        }

        flyout.Hide();
    }

    private static T? FindAncestor<T>(Interactive? interactive) where T : Interactive
    {
        while (interactive is not null)
        {
            if (interactive is T match)
            {
                return match;
            }

            interactive = interactive.Parent as Interactive;
        }

        return null;
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

    private async Task ShowErrorDialogAsync(string message)
    {
        var dialog = new Window
        {
            Title = AppServices.I18n.T("host/dialog/errorTitle"),
            Width = 460,
            MinHeight = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(16),
                LastChildFill = true,
                Children =
                {
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        MinWidth = 90,
                        [DockPanel.DockProperty] = Dock.Bottom
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                }
            }
        };

        if (dialog.Content is DockPanel dock && dock.Children.FirstOrDefault() is Button okBtn)
        {
            okBtn.Click += (_, _) => dialog.Close();
        }

        await dialog.ShowDialog(this);
    }

    private async void OnNamingTemplateTagPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        // Clicking the delete button should not start dragging.
        if (e.Source is Button)
        {
            return;
        }

        if (sender is not Border b || b.DataContext is not NamingTemplateTokenVm token)
        {
            return;
        }

        _draggingToken = token;
        _draggingTokenChip = b;
        b.Classes.Add("dragging");

        try
        {
            // Payload isn't used for ordering; the VM state + token reference are enough.
            var dragData = new DataTransfer();
            dragData.Add(DataTransferItem.CreateText("convertool-naming-template-token"));

            _ = await DragDrop.DoDragDropAsync(
                e,
                dragData,
                DragDropEffects.Move);
        }
        finally
        {
            b.Classes.Remove("dragging");
            _draggingToken = null;
            _draggingTokenChip = null;

            if (_hoveredTokenChip is { } hb)
            {
                hb.Classes.Remove("dropzone");
                _hoveredTokenChip = null;
            }
        }

        e.Handled = true;
    }

    private void OnNamingTemplateTagDragOver(object? sender, DragEventArgs e)
    {
        if (_vm is null || _draggingToken is null)
        {
            return;
        }

        if (sender is not Border b || b.DataContext is not NamingTemplateTokenVm target)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;

        // Reorder immediately to create a visual gap.
        if (!ReferenceEquals(_draggingToken, target))
        {
            _vm.MoveNamingTemplateToken(_draggingToken, target);
        }

        if (!ReferenceEquals(_hoveredTokenChip, b))
        {
            _hoveredTokenChip?.Classes.Remove("dropzone");
            _hoveredTokenChip = b;
            _hoveredTokenChip.Classes.Add("dropzone");
        }
    }

    private void OnNamingTemplateTagDrop(object? sender, DragEventArgs e)
    {
        if (_vm is null || _draggingToken is null)
        {
            return;
        }

        if (sender is Border b && b.DataContext is NamingTemplateTokenVm target && !ReferenceEquals(_draggingToken, target))
        {
            _vm.MoveNamingTemplateToken(_draggingToken, target);
        }

        if (sender is Border sb)
        {
            sb.Classes.Remove("dropzone");
        }

        e.Handled = true;
    }

}