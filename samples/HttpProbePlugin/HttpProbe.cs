using System.Diagnostics;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;

namespace HttpProbePlugin;

/// <summary>
/// Web endpoint check loaded as a plugin. Reports <c>up</c> (2xx/3xx), <c>status_code</c> and <c>response_ms</c>.
/// </summary>
/// <remarks>Settings: <c>url</c> (default <c>http://{Host}/</c>), <c>timeoutMs</c> (default 5000).</remarks>
public sealed class HttpProbe : IProbe
{
    // One shared client for all devices: avoids socket exhaustion from per-call HttpClient instances.
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string Type => "http";

    public async Task<ProbeResult> CollectAsync(DeviceDefinition device, ProbeDefinition probe, CancellationToken cancellationToken)
    {
        var url = probe.GetString("url", $"http://{device.Host}/");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(probe.GetInt("timeoutMs", 5000));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            stopwatch.Stop();

            var code = (int)response.StatusCode;
            var up = code is >= 200 and < 400;
            Metric[] metrics =
            [
                Metric.Bool("up", up),
                new("status_code", code, "code"),
                new("response_ms", stopwatch.Elapsed.TotalMilliseconds, "ms"),
            ];

            return up ? ProbeResult.Ok(metrics) : ProbeResult.Failed($"HTTP {code}", metrics);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeResult.Failed($"Timeout calling {url}", Metric.Bool("up", false));
        }
        catch (HttpRequestException ex)
        {
            return ProbeResult.Failed(ex.Message, Metric.Bool("up", false));
        }
    }
}
