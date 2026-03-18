using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Host.Plugins;
using PluginAbstractions;

namespace Host;

public sealed class PluginLoadHandle : IDisposable
{
    private AssemblyLoadContext? _alc;

    internal PluginLoadHandle(IConverterPlugin instance, AssemblyLoadContext alc)
    {
        Instance = instance;
        _alc = alc;
    }

    public IConverterPlugin Instance { get; }

    public void Dispose()
    {
        if (_alc is null)
        {
            return;
        }

        _alc.Unload();
        _alc = null;

        // Help GC collect the collectible ALC deterministically.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginAssemblyPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Ensure the plugin uses the same PluginAbstractions assembly as the Host.
        if (string.Equals(assemblyName.Name, "PluginAbstractions", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}

public static class PluginRuntimeLoader
{
    public static PluginLoadHandle? TryLoadPlugin(PluginEntry entry)
    {
        try
        {
            var assemblyPath = Path.Combine(entry.PluginDir, entry.Manifest.Assembly);
            if (!File.Exists(assemblyPath))
            {
                Console.WriteLine($"[plugin-load] Assembly not found: {assemblyPath}");
                return null;
            }

            var alc = new PluginLoadContext(assemblyPath);
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            var type = asm.GetType(entry.Manifest.Type, throwOnError: false);
            if (type is null)
            {
                Console.WriteLine($"[plugin-load] Type not found: {entry.Manifest.Type}");
                alc.Unload();
                return null;
            }

            if (!typeof(IConverterPlugin).IsAssignableFrom(type))
            {
                Console.WriteLine($"[plugin-load] Type does not implement IConverterPlugin: {entry.Manifest.Type}");
                alc.Unload();
                return null;
            }

            var instance = Activator.CreateInstance(type) as IConverterPlugin;
            if (instance is null)
            {
                Console.WriteLine($"[plugin-load] Failed to create instance: {entry.Manifest.Type}");
                alc.Unload();
                return null;
            }

            return new PluginLoadHandle(instance, alc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[plugin-load] Failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}

