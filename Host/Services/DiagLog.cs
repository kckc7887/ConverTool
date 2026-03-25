using System;
using System.IO;
using System.Text;

namespace Host.Services;

internal static class DiagLog
{
    private static readonly object _lock = new();

    private static string GetLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConverTool");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "contextmenu-ipc.log");
    }

    public static void Write(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [pid:{Environment.ProcessId}] {message}";
            lock (_lock)
            {
                File.AppendAllText(GetLogPath(), line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // never crash the app due to diagnostics
        }
    }
}

