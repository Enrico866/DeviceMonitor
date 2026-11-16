using DeviceMonitor.Abstractions.Configuration;

namespace DeviceMonitor.Abstractions.Probing;

/// <summary>
/// Collects metrics from a device. Implementations are discovered by reflection, both in the
/// built-in probes assembly and in any plugin DLL dropped in the plugins folder, so they must
/// have a public parameterless constructor and be safe to call concurrently.
/// </summary>
public interface IProbe
{
    /// <summary>Type name referenced by <see cref="ProbeDefinition.Type"/>.</summary>
    string Type { get; }

    /// <summary>
    /// Polls the device once. Network failures are reported through <see cref="ProbeResult.Failed"/>
    /// rather than thrown, so one unreachable device never stops the polling loop.
    /// </summary>
    Task<ProbeResult> CollectAsync(DeviceDefinition device, ProbeDefinition probe, CancellationToken cancellationToken);
}
