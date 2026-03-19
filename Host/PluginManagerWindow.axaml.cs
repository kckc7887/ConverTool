using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Host.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Host;

public partial class PluginManagerWindow : Window
{
    public PluginManagerWindow()
    {
        InitializeComponent();
        DataContext = new PluginManagerViewModel(AppServices.I18n, AppServices.PluginI18n);
    }

    private async void OnAddPlugin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagerViewModel vm)
        {
            return;
        }

        try
        {
            // On some platforms / nested ShowDialog scenarios, Window.StorageProvider can be null.
            // TopLevel.GetTopLevel is the documented way to obtain a working StorageProvider.
            var top = TopLevel.GetTopLevel(this);
            if (top is null && Owner is TopLevel ownerTop)
            {
                top = ownerTop;
            }

            if (top?.StorageProvider is null)
            {
                vm.SetStatusMessage(AppServices.I18n.T("host/pluginManager/pickerUnavailable"));
                return;
            }

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = AppServices.I18n.T("host/pluginManager/addPickerTitle"),
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

            var error = await vm.InstallFromZipAsync(zip.Path.LocalPath);
            if (!string.IsNullOrWhiteSpace(error))
            {
                await ShowErrorDialogAsync(error);
            }
        }
        catch (Exception ex)
        {
            vm.SetStatusMessage($"{ex.GetType().Name}: {ex.Message}");
            await ShowErrorDialogAsync($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnPluginZipDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        var hasZip = files?.Any(f =>
            f.Path.LocalPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ?? false;

        e.DragEffects = hasZip ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnPluginZipDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PluginManagerViewModel vm)
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            vm.SetStatusMessage(AppServices.I18n.T("host/pluginManager/selectZip"));
            e.Handled = true;
            return;
        }

        var zips = files
            .Select(f => f.Path.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p) && p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (zips.Length == 0)
        {
            vm.SetStatusMessage(AppServices.I18n.T("host/pluginManager/selectZip"));
            e.Handled = true;
            return;
        }

        foreach (var zipPath in zips)
        {
            var error = await vm.InstallFromZipAsync(zipPath);
            if (!string.IsNullOrWhiteSpace(error))
            {
                await ShowErrorDialogAsync(error);
                break;
            }
        }

        e.Handled = true;
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
                Margin = new Avalonia.Thickness(16),
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
}

