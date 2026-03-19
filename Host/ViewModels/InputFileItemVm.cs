using System;
using System.IO;
using System.Windows.Input;

namespace Host.ViewModels;

/// <summary>单条输入文件：用于列表展示（图标 + 文件名）与移除。</summary>
public sealed class InputFileItemVm
{
    public InputFileItemVm(string fullPath, Action<InputFileItemVm> onRemove)
    {
        FullPath = fullPath;
        RemoveCommand = new SyncCommand(() => onRemove(this));
    }

    public string FullPath { get; }

    public string FileName => Path.GetFileName(FullPath);

    public ICommand RemoveCommand { get; }
}
