using System.Net.NetworkInformation;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;

namespace DeviceMonitor.Probes;

/// <summary>
/// Reachability via ICMP echo. Sends <c>count</c> pings and reports
/// <c>reachable</c> (bool), <c>rtt_ms</c> (average of the replies) and <c>packet_loss_percent</c>.
/// </summary>
/// <remarks>Settings: <c>count</c> (default 3), <c>timeoutMs</c> (default 1000).</remarks>
public sealed class IcmpProbe : IProbe
{
    public string Type => "icmp";

    public async Task<ProbeResult> CollectAsync(DeviceDefinition device, ProbeDefinition probe, CancellationToken cancellationToken)
    {
        var count = Math.Max(1, probe.GetInt("count", 3));
        var timeout = TimeSpan.FromMilliseconds(probe.GetInt("timeoutMs", 1000));

        using var ping = new Ping();
        var roundTrips = new List<long>(count);
        string? lastError = null;

        for (var i = 0; i < count; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(device.Host, timeout, cancellationToken: cancellationToken);
                if (reply.Status == IPStatus.Success)
                {
                    roundTrips.Add(reply.RoundtripTime);
                }
                else
                {
                    lastError = reply.Status.ToString();
                }
            }
            catch (PingException ex)
            {
                lastError = ex.InnerException?.Message ?? ex.Message;
            }
        }

        var lossPercent = 100.0 * (count - roundTrips.Count) / count;
        var lossMetric = new Metric("packet_loss_percent", lossPercent, "%");

        if (roundTrips.Count == 0)
        {
            return ProbeResult.Failed(lastError ?? "No reply", Metric.Bool("reachable", false), lossMetric);
        }

        return ProbeResult.Ok(
            Metric.Bool("reachable", true),
            new Metric("rtt_ms", roundTrips.Average(), "ms"),
            lossMetric);
    }
}
