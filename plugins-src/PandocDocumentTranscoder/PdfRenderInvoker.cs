using System.Reflection;
using System.Runtime.Loader;
using PluginAbstractions;

namespace PandocDocumentTranscoder;

internal static class PdfRenderInvoker
{
    private static readonly object InitLock = new();
    private static Assembly? _assembly;
    private static MethodInfo? _convertAsync;

    public static async Task InvokeAsync(
        string cacheDir,
        ExecuteContext context,
        string pdfTargetId,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        PrependNativeSearchPath(cacheDir);

        MethodInfo convert;
        lock (InitLock)
        {
            if (_assembly is null)
            {
                var dll = Path.Combine(cacheDir, "PandocDocumentTranscoder.PdfRender.dll");
                if (!File.Exists(dll))
                {
                    throw new InvalidOperationException(
                        $"Missing PDF render assembly: {dll}. Download the bundle or set CONVERTOOL_PDF_RENDER_DIR.");
                }

                var ctx = new PdfRenderAssemblyLoadContext(dll);
                _assembly = ctx.LoadFromAssemblyPath(dll);
                var t = _assembly.GetType("PandocDocumentTranscoder.PdfRender.PdfToImageConverter", throwOnError: true);
                _convertAsync = t!.GetMethod("ConvertAsync", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("PdfToImageConverter.ConvertAsync not found.");
            }

            convert = _convertAsync!;
        }

        var result = convert.Invoke(null, new object[] { context, pdfTargetId, reporter, cancellationToken });
        if (result is Task tsk)
            await tsk.ConfigureAwait(false);
    }

    private static void PrependNativeSearchPath(string cacheDir)
    {
        // Publish layout and NuGet pull both place pdfium.dll / libSkiaSharp.dll next to managed DLLs.
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (path.IndexOf(cacheDir, StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        Environment.SetEnvironmentVariable("PATH", cacheDir + Path.PathSeparator + path);
    }

    private sealed class PdfRenderAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PdfRenderAssemblyLoadContext(string pdfRenderDllPath) : base("pdf-render", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pdfRenderDllPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == "PluginAbstractions")
                return Default.LoadFromAssemblyName(assemblyName);

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
                return LoadFromAssemblyPath(path);

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null && File.Exists(path))
                return LoadUnmanagedDllFromPath(path);

            return base.LoadUnmanagedDll(unmanagedDllName);
        }
    }
}
