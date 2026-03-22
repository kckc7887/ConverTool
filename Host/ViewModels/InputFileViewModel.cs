using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Host.ViewModels;

public sealed class InputFileViewModel : ObservableObject
{
    public ObservableCollection<InputFileItemVm> InputFiles { get; } = new();

    public bool HasInputFiles => InputFiles.Count > 0;

    public bool HasNoInputFiles => InputFiles.Count == 0;

    public event EventHandler? InputFilesChanged;

    public event EventHandler<UnsupportedFormatEventArgs>? UnsupportedFormatDetected;

    public TopLevel? TopLevel { get; set; }

    private readonly HashSet<string> _supportedExtensions;

    public InputFileViewModel(IEnumerable<string> supportedExtensions)
    {
        _supportedExtensions = new HashSet<string>(supportedExtensions, StringComparer.OrdinalIgnoreCase);
        InputFiles.CollectionChanged += OnInputFilesCollectionChanged;
    }

    public void UpdateSupportedExtensions(IEnumerable<string> supportedExtensions)
    {
        _supportedExtensions.Clear();
        foreach (var ext in supportedExtensions)
        {
            var normalized = ext.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(normalized))
            {
                _supportedExtensions.Add(normalized);
            }
        }
    }

    private void OnInputFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasInputFiles));
        RaisePropertyChanged(nameof(HasNoInputFiles));
        InputFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveInputFile(InputFileItemVm item)
    {
        InputFiles.Remove(item);
    }

    public void AddInputPaths(IEnumerable<string> paths)
    {
        var unsupportedFiles = new List<string>();

        foreach (var p in paths)
        {
            var trimmed = (p ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (InputFiles.Any(x => string.Equals(x.FullPath, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var ext = Path.GetExtension(trimmed);
            if (!string.IsNullOrEmpty(ext))
            {
                ext = ext.TrimStart('.').ToLowerInvariant();
            }

            if (!string.IsNullOrEmpty(ext) && _supportedExtensions.Contains(ext))
            {
                InputFiles.Add(new InputFileItemVm(trimmed, RemoveInputFile));
            }
            else
            {
                unsupportedFiles.Add(Path.GetFileName(trimmed));
            }
        }

        if (unsupportedFiles.Count > 0)
        {
            UnsupportedFormatDetected?.Invoke(this, new UnsupportedFormatEventArgs(unsupportedFiles));
        }
    }

    public void Clear()
    {
        InputFiles.Clear();
    }

    public async Task BrowseInputAsync()
    {
        if (TopLevel?.StorageProvider is null)
        {
            return;
        }

        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true
        });

        AddInputPaths(files.Select(f => f.Path.LocalPath));
    }
}

public sealed class UnsupportedFormatEventArgs : EventArgs
{
    public IReadOnlyList<string> UnsupportedFiles { get; }

    public UnsupportedFormatEventArgs(IReadOnlyList<string> unsupportedFiles)
    {
        UnsupportedFiles = unsupportedFiles;
    }
}
