namespace DeviceMonitor.Abstractions.Probing;

public sealed record ProbeResult(bool Success, IReadOnlyList<Metric> Metrics, string? Error = null)
{
    public static ProbeResult Ok(params Metric[] metrics) => new(true, metrics);

    /// <summary>A failed poll may still carry metrics, e.g. <c>reachable = 0</c>.</summary>
    public static ProbeResult Failed(string error, params Metric[] metrics) => new(false, metrics, error);
}
