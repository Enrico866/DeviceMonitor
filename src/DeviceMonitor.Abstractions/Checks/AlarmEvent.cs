using DeviceMonitor.Abstractions.Configuration;

namespace DeviceMonitor.Abstractions.Checks;

public enum AlarmKind
{
    Raised,
    Cleared,
}

public sealed record AlarmEvent(
    string DeviceId,
    string Metric,
    AlarmKind Kind,
    Severity Severity,
    double Value,
    double Threshold,
    string Description,
    DateTimeOffset Timestamp);
