namespace DeviceMonitor.Abstractions.Configuration;

/// <summary>
/// One probe attached to a device, for example <c>icmp</c> or <c>snmp</c>.
/// </summary>
public sealed class ProbeDefinition
{
    /// <summary>Probe type, matched case-insensitively against <see cref="Probing.IProbe.Type"/>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Prefix of the metrics this probe produces. Defaults to <see cref="Type"/>;
    /// set it when a device has two probes of the same type (e.g. two TCP ports).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Probe specific settings (port, community, timeout...).</summary>
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string MetricPrefix => string.IsNullOrWhiteSpace(Name) ? Type : Name;
}
