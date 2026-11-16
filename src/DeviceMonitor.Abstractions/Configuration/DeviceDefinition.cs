namespace DeviceMonitor.Abstractions.Configuration;

/// <summary>
/// A device to monitor, as declared in configuration.
/// </summary>
public sealed class DeviceDefinition
{
    /// <summary>Unique, stable identifier used in the API and in stored data.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human readable name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Host name or IP address the probes connect to.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Seconds between two polling cycles.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Probes run on every polling cycle.</summary>
    public List<ProbeDefinition> Probes { get; set; } = [];

    /// <summary>Threshold checks evaluated against the metrics the probes collect.</summary>
    public List<CheckDefinition> Checks { get; set; } = [];
}
