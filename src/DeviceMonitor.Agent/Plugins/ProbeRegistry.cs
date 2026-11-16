using System.Reflection;
using DeviceMonitor.Abstractions.Probing;
using DeviceMonitor.Probes;

namespace DeviceMonitor.Agent.Plugins;

/// <summary>
/// All probes available to the agent: the built-in ones plus those found in the plugins folder.
/// </summary>
public sealed class ProbeRegistry
{
    private readonly Dictionary<string, IProbe> _probes = new(StringComparer.OrdinalIgnoreCase);

    public ProbeRegistry(IEnumerable<IProbe> probes)
    {
        foreach (var probe in probes)
        {
            if (!_probes.TryAdd(probe.Type, probe))
            {
                throw new InvalidOperationException($"Two probes declare the type '{probe.Type}'.");
            }
        }
    }

    public IReadOnlyCollection<string> Types => _probes.Keys;

    public IProbe Get(string type) =>
        _probes.TryGetValue(type, out var probe) ? probe : throw new KeyNotFoundException($"Unknown probe type '{type}'.");

    public static ProbeRegistry Load(string pluginsPath, ILogger logger)
    {
        var probes = CreateProbes(typeof(IcmpProbe).Assembly).ToList();

        var fullPath = Path.GetFullPath(pluginsPath, AppContext.BaseDirectory);
        if (Directory.Exists(fullPath))
        {
            foreach (var pluginDirectory in Directory.GetDirectories(fullPath))
            {
                var name = Path.GetFileName(pluginDirectory);
                var assemblyPath = Path.Combine(pluginDirectory, name + ".dll");
                if (!File.Exists(assemblyPath))
                {
                    logger.LogWarning("Plugin folder {Folder} skipped: expected {Assembly}", pluginDirectory, assemblyPath);
                    continue;
                }

                var assembly = new PluginLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
                var found = CreateProbes(assembly).ToList();
                logger.LogInformation("Loaded plugin {Plugin} with probes: {Probes}", name, string.Join(", ", found.Select(p => p.Type)));
                probes.AddRange(found);
            }
        }

        return new ProbeRegistry(probes);
    }

    private static IEnumerable<IProbe> CreateProbes(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IProbe).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IProbe)Activator.CreateInstance(t)!);
}
