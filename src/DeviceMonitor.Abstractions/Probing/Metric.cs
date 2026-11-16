namespace DeviceMonitor.Abstractions.Probing;

/// <param name="Name">Metric name without the probe prefix, e.g. <c>rtt_ms</c>.</param>
/// <param name="Value">Numeric value; booleans are reported as 1/0.</param>
/// <param name="Unit">Unit of measure, e.g. <c>ms</c>, <c>%</c>, <c>s</c>, <c>bool</c>.</param>
public sealed record Metric(string Name, double Value, string Unit)
{
    public static Metric Bool(string name, bool value) => new(name, value ? 1 : 0, "bool");
}
