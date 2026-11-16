using DeviceMonitor.Abstractions.Configuration;

namespace DeviceMonitor.Agent.Configuration;

/// <summary>The <c>Monitoring</c> section of appsettings.json.</summary>
public sealed class MonitoringConfig
{
    public const string SectionName = "Monitoring";

    /// <summary>Folder scanned for probe plugins, relative to the application folder. Layout: <c>plugins/&lt;Name&gt;/&lt;Name&gt;.dll</c>.</summary>
    public string PluginsPath { get; set; } = "plugins";

    /// <summary>Folder where the JSON-lines sink writes samples and alarms. Empty disables the file sink.</summary>
    public string DataDirectory { get; set; } = "data";

    public List<DeviceDefinition> Devices { get; set; } = [];

    /// <summary>Fails fast at startup on configuration mistakes instead of logging them on every poll.</summary>
    public void Validate(IReadOnlyCollection<string> knownProbeTypes)
    {
        var errors = new List<string>();

        foreach (var duplicate in Devices.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate device id '{duplicate.Key}'.");
        }

        foreach (var device in Devices)
        {
            var label = string.IsNullOrWhiteSpace(device.Id) ? "<no id>" : device.Id;
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                errors.Add("A device has no Id.");
            }

            if (string.IsNullOrWhiteSpace(device.Host))
            {
                errors.Add($"Device '{label}': Host is required.");
            }

            if (device.IntervalSeconds < 1)
            {
                errors.Add($"Device '{label}': IntervalSeconds must be at least 1.");
            }

            foreach (var probe in device.Probes.Where(p => !knownProbeTypes.Contains(p.Type, StringComparer.OrdinalIgnoreCase)))
            {
                errors.Add($"Device '{label}': unknown probe type '{probe.Type}'. Available: {string.Join(", ", knownProbeTypes)}.");
            }

            var prefixes = device.Probes.Select(p => p.MetricPrefix).ToList();
            foreach (var duplicate in prefixes.GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                errors.Add($"Device '{label}': two probes share the metric prefix '{duplicate.Key}'; set a distinct Name on each.");
            }

            foreach (var check in device.Checks)
            {
                var prefix = check.Metric.Split('.', 2)[0];
                if (!prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Device '{label}': check on '{check.Metric}' does not match any probe (expected '<probe>.<metric>').");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid monitoring configuration:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }
}
