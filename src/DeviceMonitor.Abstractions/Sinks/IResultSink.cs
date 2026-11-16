using DeviceMonitor.Abstractions.Checks;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;

namespace DeviceMonitor.Abstractions.Sinks;

/// <summary>
/// Persists or forwards what the probes collected and the alarms the checks produced
/// (console, file, database, message bus...). Every registered sink receives every item.
/// </summary>
public interface IResultSink
{
    Task WriteSampleAsync(DeviceDefinition device, ProbeDefinition probe, ProbeResult result, DateTimeOffset timestamp, CancellationToken cancellationToken);

    Task WriteAlarmAsync(AlarmEvent alarm, CancellationToken cancellationToken);
}
