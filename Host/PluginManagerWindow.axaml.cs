using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

            await vm.InstallFromZipAsync(zip.Path.LocalPath);
        }
        catch (Exception ex)
        {
            vm.SetStatusMessage($"{ex.GetType().Name}: {ex.Message}");
        }
    }
}

