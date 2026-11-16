using System.Diagnostics;
using System.Net.Sockets;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;

namespace DeviceMonitor.Probes;

/// <summary>
/// Service availability via a TCP connect. Reports <c>open</c> (bool) and <c>connect_ms</c>.
/// </summary>
/// <remarks>Settings: <c>port</c> (required), <c>timeoutMs</c> (default 2000).</remarks>
public sealed class TcpPortProbe : IProbe
{
    public string Type => "tcp";

    public async Task<ProbeResult> CollectAsync(DeviceDefinition device, ProbeDefinition probe, CancellationToken cancellationToken)
    {
        var port = probe.GetRequiredInt("port");
        var timeout = TimeSpan.FromMilliseconds(probe.GetInt("timeoutMs", 2000));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(device.Host, port, timeoutCts.Token);
            stopwatch.Stop();
            return ProbeResult.Ok(
                Metric.Bool("open", true),
                new Metric("connect_ms", stopwatch.Elapsed.TotalMilliseconds, "ms"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeResult.Failed($"Timeout connecting to port {port}", Metric.Bool("open", false));
        }
        catch (SocketException ex)
        {
            return ProbeResult.Failed($"Port {port}: {ex.SocketErrorCode}", Metric.Bool("open", false));
        }
    }
}
