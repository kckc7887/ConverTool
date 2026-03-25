using Avalonia;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using Host.Plugins;
using Host.Services;

namespace Host;

class Program
{
    private const string PipeName = "ConverTool-SingleInstance";
    private const string MutexName = "ConverTool-SingleInstance-Mutex";
    private static readonly object _initialArgsLock = new();
    private static string[]? _initialArgsForFirstInstance;
    private static readonly ConcurrentQueue<string[]> _incomingArgsQueue = new();
    private static volatile bool _avaloniaReady;

    public static string[]? ConsumeInitialArgsForFirstInstance()
    {
        lock (_initialArgsLock)
        {
            var v = _initialArgsForFirstInstance;
            _initialArgsForFirstInstance = null;
            return v;
        }
    }

    public static void MarkAvaloniaReadyAndDrainIncoming()
    {
        _avaloniaReady = true;
        DrainIncomingArgsOnUiThread();
    }

    private static void EnqueueIncomingArgs(string[] args)
    {
        _incomingArgsQueue.Enqueue(args);
        if (_avaloniaReady)
        {
            DrainIncomingArgsOnUiThread();
        }
    }

    private static void DrainIncomingArgsOnUiThread()
    {
        try
        {
            // Important: do NOT touch Dispatcher until Avalonia is ready.
            if (!_avaloniaReady)
                return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                while (_incomingArgsQueue.TryDequeue(out var a))
                {
                    try
                    {
                        if (App.Current is Host.App app)
                        {
                            app.HandleCommandLineArgs(a);
                        }
                        else
                        {
                            DiagLog.Write("DrainIncomingArgs: App.Current is not Host.App");
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagLog.Write("DrainIncomingArgs: exception " + ex);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            DiagLog.Write("DrainIncomingArgsOnUiThread outer exception: " + ex);
        }
    }
    
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            DiagLog.Write($"Main start. args=[{string.Join(" | ", args.Select(a => a ?? "<null>"))}]");

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                DiagLog.Write("UnhandledException: " + (e.ExceptionObject?.ToString() ?? "<null>"));
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                DiagLog.Write("UnobservedTaskException: " + e.Exception);
            };

            // 使用全局 Mutex 避免“启动竞态”：
            // 右键多选可能会几乎同时启动多个进程；仅依赖管道 connect(100ms) 很容易误判。
            using var mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
            if (!createdNew)
            {
                DiagLog.Write("Not first instance. Forwarding args to existing instance then exiting.");
                // 已有实例（可能仍在启动中）。尝试把参数转发给主实例并退出。
                ForwardArgsToExistingInstance(args);
                return;
            }

            DiagLog.Write("First instance acquired mutex.");
            
            // 统一右键多选/单选的行为：
            // - “有进程”时：通过管道投递 args -> App.HandleCommandLineArgs
            // - “无进程”时：先启动主实例（不带 args），然后在 App 初始化完成后走同一条 HandleCommandLineArgs 路径
            if (args.Length >= 2 && string.Equals(args[0], "convert", StringComparison.OrdinalIgnoreCase))
            {
                // 清洗 + 去重：Explorer 多选下可能重复传入第一个文件（"%1" + %*）
                var filePaths = args
                    .Skip(1)
                    .Select(p => (p ?? "").Trim().Trim('"'))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                lock (_initialArgsLock)
                {
                    _initialArgsForFirstInstance = filePaths.Length > 0
                        ? ["convert", .. filePaths]
                        : null;
                }

                DiagLog.Write($"Captured initial convert args. fileCount={filePaths.Length}");
                // 清空 args，避免 Avalonia 自己解析这些参数
                args = Array.Empty<string>();
            }
            
            // 启动命名管道服务器
            DiagLog.Write("Starting pipe server thread.");
            StartPipeServer();
            
            ServiceLocator.Initialize(AppContext.BaseDirectory);
            ServiceLocator.GetService<PluginCatalog>().PrintSummary();

            DiagLog.Write("Starting Avalonia lifetime.");
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
                var text =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Startup failed\r\n"
                    + $"BaseDirectory: {AppContext.BaseDirectory}\r\n"
                    + $"CurrentDirectory: {Environment.CurrentDirectory}\r\n"
                    + $"Args: {string.Join(" ", args)}\r\n\r\n"
                    + ex;
                File.WriteAllText(logPath, text);
            }
            catch
            {
            }

            Console.Error.WriteLine(ex);
            DiagLog.Write("Main exception: " + ex);
            throw;
        }
    }

    private static void ForwardArgsToExistingInstance(string[] args)
    {
        // 可能遇到“主实例已拿到 Mutex 但管道服务还没 ready”的极短窗口期，
        // 因此采用短时间重试，而不是 100ms 超时后直接当作无实例继续启动。
        var deadline = Environment.TickCount64 + 3000; // ~3s

        var attempt = 0;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                attempt++;
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(200);

                using var writer = new StreamWriter(client);
                {
                    writer.WriteLine(args.Length);
                    foreach (var arg in args)
                    {
                        writer.WriteLine(arg);
                    }
                    writer.Flush();
                }

                DiagLog.Write($"Forward success. attempts={attempt} argsLen={args.Length}");
                return;
            }
            catch
            {
                if (attempt == 1 || attempt % 10 == 0)
                {
                    DiagLog.Write($"Forward attempt failed. attempt={attempt}");
                }
                Thread.Sleep(50);
            }
        }

        // 转发失败：不再启动新 UI，以避免“多实例并发启动”导致的卡死/闪退。
        // 这里保持静默退出即可（Explorer 右键体验更友好）。
        DiagLog.Write($"Forward failed (timeout). argsLen={args.Length}");
    }
    
    private static void StartPipeServer()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                while (true)
                {
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In))
                    {
                        server.WaitForConnection();
                        
                        try
                        {
                            using (var reader = new StreamReader(server))
                            {
                                // 读取参数数量
                                string? countLine = reader.ReadLine();
                                if (!string.IsNullOrEmpty(countLine) && int.TryParse(countLine, out int count))
                                {
                                    // 读取每个参数
                                    var args = new string[count];
                                    for (int i = 0; i < count; i++)
                                    {
                                        args[i] = reader.ReadLine() ?? "";
                                    }
                                    DiagLog.Write($"Pipe received argsLen={args.Length} head={(args.Length > 0 ? args[0] : "")}");
                                    // Do not call Dispatcher before Avalonia is initialized; enqueue for later processing.
                                    EnqueueIncomingArgs(args);
                                }
                                else
                                {
                                    DiagLog.Write($"Pipe received invalid countLine='{countLine ?? "<null>"}'");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error handling pipe message: {ex.Message}");
                            DiagLog.Write("Pipe handling exception: " + ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pipe server error: {ex.Message}");
                DiagLog.Write("Pipe server loop exception: " + ex);
            }
        });
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
