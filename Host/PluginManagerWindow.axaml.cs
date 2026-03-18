using Avalonia.Controls;
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
        DataContext = new PluginManagerViewModel(AppServices.I18n);
    }

    private async void OnAddPlugin(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false
        });

        var zip = files.FirstOrDefault();
        if (zip is null)
        {
            return;
        }

        if (DataContext is PluginManagerViewModel vm)
        {
            await vm.InstallFromZipAsync(zip.Path.LocalPath);
        }
    }
}

