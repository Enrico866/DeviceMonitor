using DeviceMonitor.Abstractions.Configuration;

namespace DeviceMonitor.Abstractions.Checks;

public enum CheckStatus
{
    /// <summary>No reading evaluated yet.</summary>
    Unknown,
    Ok,
    Alarm,
}

/// <summary>
/// Runtime state of one check on one device. Implements the OK/ALARM state machine:
/// an alarm is raised after <see cref="CheckDefinition.FailuresBeforeAlarm"/> consecutive bad
/// readings and cleared by the first good one. An event is emitted only when the state changes.
/// </summary>
public sealed class CheckState(string deviceId, CheckDefinition definition)
{
    private readonly Lock _sync = new();

    public string DeviceId { get; } = deviceId;
    public CheckDefinition Definition { get; } = definition;
    public CheckStatus Status { get; private set; } = CheckStatus.Unknown;
    public int ConsecutiveFailures { get; private set; }
    public double? LastValue { get; private set; }
    public DateTimeOffset? LastEvaluatedAt { get; private set; }
    public DateTimeOffset? StatusSince { get; private set; }

    /// <returns>The alarm event when the status changed to or from <see cref="CheckStatus.Alarm"/>, otherwise null.</returns>
    public AlarmEvent? Evaluate(double value, DateTimeOffset now)
    {
        lock (_sync)
        {
            LastValue = value;
            LastEvaluatedAt = now;

            if (!Definition.AlarmWhen.Matches(value, Definition.Threshold))
            {
                ConsecutiveFailures = 0;
                var wasAlarm = Status == CheckStatus.Alarm;
                SetStatus(CheckStatus.Ok, now);
                return wasAlarm ? CreateEvent(AlarmKind.Cleared, value, now) : null;
            }

            ConsecutiveFailures++;
            if (Status == CheckStatus.Alarm)
            {
                return null;
            }

            if (ConsecutiveFailures < Math.Max(1, Definition.FailuresBeforeAlarm))
            {
                // Bad reading but still within the tolerated retries: the check stays OK.
                SetStatus(CheckStatus.Ok, now);
                return null;
            }

            SetStatus(CheckStatus.Alarm, now);
            return CreateEvent(AlarmKind.Raised, value, now);
        }
    }

    private void SetStatus(CheckStatus status, DateTimeOffset now)
    {
        if (Status != status)
        {
            Status = status;
            StatusSince = now;
        }
    }

    private AlarmEvent CreateEvent(AlarmKind kind, double value, DateTimeOffset now) =>
        new(DeviceId, Definition.Metric, kind, Definition.Severity, value, Definition.Threshold, Definition.DisplayName, now);
}
