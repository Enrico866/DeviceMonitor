using System.Collections.Concurrent;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;

namespace DeviceMonitor.Probes;

/// <summary>
/// Fake device for demos and tests: a CPU random walk with occasional spikes and a temperature
/// that follows it, so threshold checks raise and clear alarms without real hardware.
/// </summary>
/// <remarks>Settings: <c>baseCpu</c> (default 30), <c>spikePercent</c> chance of a spike per poll (default 15).</remarks>
public sealed class SimulatedProbe : IProbe
{
    private readonly ConcurrentDictionary<string, double> _cpuByDevice = new();

    public string Type => "simulated";

    public Task<ProbeResult> CollectAsync(DeviceDefinition device, ProbeDefinition probe, CancellationToken cancellationToken)
    {
        var baseCpu = probe.GetInt("baseCpu", 30);
        var spikePercent = probe.GetInt("spikePercent", 15);

        var cpu = _cpuByDevice.AddOrUpdate(
            device.Id,
            baseCpu,
            (_, previous) =>
            {
                if (Random.Shared.Next(100) < spikePercent)
                {
                    return Random.Shared.Next(85, 100);
                }

                // Drift back towards the base load with some noise.
                var next = previous + ((baseCpu - previous) * 0.5) + Random.Shared.Next(-8, 9);
                return Math.Clamp(next, 0, 100);
            });

        var temperature = 35 + (cpu * 0.4) + Random.Shared.NextDouble();

        return Task.FromResult(ProbeResult.Ok(
            new Metric("cpu_load_percent", Math.Round(cpu, 1), "%"),
            new Metric("temperature_c", Math.Round(temperature, 1), "°C")));
    }
}
