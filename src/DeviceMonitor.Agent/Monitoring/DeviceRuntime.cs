using System.Collections.Concurrent;
using DeviceMonitor.Abstractions.Checks;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;

namespace DeviceMonitor.Agent.Monitoring;

public enum DeviceStatus
{
    Unknown,
    Ok,

    /// <summary>At least one probe failed, but no check is in alarm.</summary>
    Degraded,
    Alarm,
}

public sealed record MetricSnapshot(double Value, string Unit, DateTimeOffset Timestamp);

public sealed record ProbeSnapshot(bool Success, string? Error, DateTimeOffset Timestamp);

/// <summary>Live state of one device: last values, last probe outcomes and check states.</summary>
public sealed class DeviceRuntime
{
    public DeviceRuntime(DeviceDefinition definition)
    {
        Definition = definition;
        Checks = definition.Checks.Select(c => new CheckState(definition.Id, c)).ToList();
    }

    public DeviceDefinition Definition { get; }

    public IReadOnlyList<CheckState> Checks { get; }

    public ConcurrentDictionary<string, MetricSnapshot> Metrics { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, ProbeSnapshot> Probes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastPollAt { get; set; }

    public DeviceStatus Status
    {
        get
        {
            if (LastPollAt is null)
            {
                return DeviceStatus.Unknown;
            }

            if (Checks.Any(c => c.Status == CheckStatus.Alarm))
            {
                return DeviceStatus.Alarm;
            }

            return Probes.Values.Any(p => !p.Success) ? DeviceStatus.Degraded : DeviceStatus.Ok;
        }
    }

    public void Record(ProbeDefinition probe, ProbeResult result, DateTimeOffset timestamp)
    {
        Probes[probe.MetricPrefix] = new ProbeSnapshot(result.Success, result.Error, timestamp);
        foreach (var metric in result.Metrics)
        {
            Metrics[$"{probe.MetricPrefix}.{metric.Name}"] = new MetricSnapshot(metric.Value, metric.Unit, timestamp);
        }
    }
}
