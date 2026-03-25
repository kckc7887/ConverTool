using System.Collections.Generic;

namespace Host;

/// <summary>
/// 用于暂存待处理的文件路径队列
/// 解决启动时带参数可能导致卡死的问题
/// </summary>
public static class PendingFilesQueue
{
    private static readonly List<string> _pendingFiles = new();
    private static readonly object _lock = new();

    public static void EnqueueFiles(IEnumerable<string> files)
    {
        lock (_lock)
        {
            _pendingFiles.AddRange(files);
        }
    }

    public static List<string> DequeueAllFiles()
    {
        lock (_lock)
        {
            var result = new List<string>(_pendingFiles);
            _pendingFiles.Clear();
            return result;
        }
    }

    public static bool HasPendingFiles
    {
        get
        {
            lock (_lock)
            {
                return _pendingFiles.Count > 0;
            }
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _pendingFiles.Clear();
        }
    }
}
