using System.Globalization;
using DeviceMonitor.Abstractions.Checks;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;
using DeviceMonitor.Abstractions.Sinks;

namespace DeviceMonitor.Agent.Sinks;

/// <summary>Writes samples (Debug) and alarms (Warning/Information) to the application log.</summary>
public sealed class LoggingSink(ILogger<LoggingSink> logger) : IResultSink
{
    public Task WriteSampleAsync(DeviceDefinition device, ProbeDefinition probe, ProbeResult result, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var values = string.Join(" ", result.Metrics.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Name}={m.Value:0.##}{m.Unit}")));
            logger.LogDebug("{Device} {Probe}: {Values}", device.Id, probe.MetricPrefix, values);
        }

        return Task.CompletedTask;
    }

    public Task WriteAlarmAsync(AlarmEvent alarm, CancellationToken cancellationToken)
    {
        if (alarm.Kind == AlarmKind.Raised)
        {
            logger.LogWarning("ALARM {Severity} on {Device}: {Description} (value {Value})", alarm.Severity, alarm.DeviceId, alarm.Description, alarm.Value);
        }
        else
        {
            logger.LogInformation("CLEARED on {Device}: {Description} (value {Value})", alarm.DeviceId, alarm.Description, alarm.Value);
        }

        return Task.CompletedTask;
    }
}
