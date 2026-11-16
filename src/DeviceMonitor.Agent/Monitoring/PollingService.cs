using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;
using DeviceMonitor.Abstractions.Sinks;
using DeviceMonitor.Agent.Plugins;

namespace DeviceMonitor.Agent.Monitoring;

/// <summary>
/// Runs one independent polling loop per device: probes in parallel, check evaluation, then fan-out
/// of samples and alarm events to every <see cref="IResultSink"/>.
/// </summary>
public sealed class PollingService(
    MonitorStore store,
    ProbeRegistry probes,
    IEnumerable<IResultSink> sinks,
    TimeProvider timeProvider,
    ILogger<PollingService> logger) : BackgroundService
{
    private readonly IReadOnlyList<IResultSink> _sinks = sinks.ToList();

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Monitoring {Count} device(s) with probes: {Probes}", store.Devices.Count, string.Join(", ", probes.Types));
        return Task.WhenAll(store.Devices.Select(d => RunDeviceLoopAsync(d, stoppingToken)));
    }

    private async Task RunDeviceLoopAsync(DeviceRuntime device, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(device.Definition.IntervalSeconds), timeProvider);
        try
        {
            do
            {
                await PollOnceAsync(device, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>One full polling cycle for a device. Exposed for tests.</summary>
    internal async Task PollOnceAsync(DeviceRuntime device, CancellationToken cancellationToken)
    {
        var definition = device.Definition;
        var results = await Task.WhenAll(definition.Probes.Select(p => RunProbeAsync(definition, p, cancellationToken)));
        var now = timeProvider.GetUtcNow();

        foreach (var (probe, result) in results)
        {
            device.Record(probe, result, now);
            if (!result.Success)
            {
                logger.LogWarning("{Device}: probe {Probe} failed: {Error}", definition.Id, probe.MetricPrefix, result.Error);
            }

            await FanOutAsync(sink => sink.WriteSampleAsync(definition, probe, result, now, cancellationToken));
        }

        device.LastPollAt = now;

        foreach (var check in device.Checks)
        {
            // A metric missing from this cycle (probe down) leaves the check state untouched:
            // reachability is what alarms on a dead device, not every other check at once.
            if (!device.Metrics.TryGetValue(check.Definition.Metric, out var snapshot) || snapshot.Timestamp != now)
            {
                continue;
            }

            var alarm = check.Evaluate(snapshot.Value, now);
            if (alarm is not null)
            {
                store.AddAlarm(alarm);
                await FanOutAsync(sink => sink.WriteAlarmAsync(alarm, cancellationToken));
            }
        }
    }

    private async Task<(ProbeDefinition Probe, ProbeResult Result)> RunProbeAsync(
        DeviceDefinition device, ProbeDefinition probe, CancellationToken cancellationToken)
    {
        try
        {
            return (probe, await probes.Get(probe.Type).CollectAsync(device, probe, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "{Device}: probe {Probe} threw", device.Id, probe.MetricPrefix);
            return (probe, ProbeResult.Failed(ex.Message));
        }
    }

    private async Task FanOutAsync(Func<IResultSink, Task> write)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                await write(sink);
            }
            catch (Exception ex)
            {
                // A broken sink (disk full, DB down) must not stop monitoring or the other sinks.
                logger.LogError(ex, "Sink {Sink} failed", sink.GetType().Name);
            }
        }
    }
}
