using System.Reflection;
using System.Runtime.Loader;
using DeviceMonitor.Abstractions.Probing;

namespace DeviceMonitor.Agent.Plugins;

/// <summary>
/// Isolated load context for one plugin: its private dependencies are resolved from its own folder
/// (via its .deps.json), so two plugins can ship different versions of the same library.
/// </summary>
internal sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(pluginPath))
{
    private static readonly string SharedContract = typeof(IProbe).Assembly.GetName().Name!;

    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // The contract must come from the host: a second copy of DeviceMonitor.Abstractions would
        // define a different IProbe type and the plugin's probes would not be recognised.
        if (assemblyName.Name == SharedContract)
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
