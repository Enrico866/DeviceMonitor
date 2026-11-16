namespace DeviceMonitor.Abstractions.Configuration;

/// <summary>
/// A threshold check: raises an alarm when <see cref="Metric"/> satisfies
/// <see cref="AlarmWhen"/> <see cref="Threshold"/> for <see cref="FailuresBeforeAlarm"/> consecutive polls.
/// </summary>
public sealed class CheckDefinition
{
    /// <summary>Full metric name, e.g. <c>icmp.reachable</c> or <c>snmp.cpu_load_percent</c>.</summary>
    public string Metric { get; set; } = string.Empty;

    public Comparison AlarmWhen { get; set; } = Comparison.GreaterThan;

    public double Threshold { get; set; }

    /// <summary>Consecutive bad readings needed to raise the alarm; filters out single spikes.</summary>
    public int FailuresBeforeAlarm { get; set; } = 1;

    public Severity Severity { get; set; } = Severity.Warning;

    /// <summary>Optional text shown in the alarm; defaults to a generated description.</summary>
    public string? Description { get; set; }

    public string DisplayName => Description ?? $"{Metric} {AlarmWhen.ToSymbol()} {Threshold}";
}

public enum Comparison
{
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Equal,
    NotEqual,
}

public enum Severity
{
    Info,
    Warning,
    Critical,
}

public static class ComparisonExtensions
{
    public static bool Matches(this Comparison comparison, double value, double threshold) => comparison switch
    {
        Comparison.GreaterThan => value > threshold,
        Comparison.GreaterOrEqual => value >= threshold,
        Comparison.LessThan => value < threshold,
        Comparison.LessOrEqual => value <= threshold,
        Comparison.Equal => value.Equals(threshold),
        Comparison.NotEqual => !value.Equals(threshold),
        _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null),
    };

    public static string ToSymbol(this Comparison comparison) => comparison switch
    {
        Comparison.GreaterThan => ">",
        Comparison.GreaterOrEqual => ">=",
        Comparison.LessThan => "<",
        Comparison.LessOrEqual => "<=",
        Comparison.Equal => "==",
        Comparison.NotEqual => "!=",
        _ => comparison.ToString(),
    };
}
