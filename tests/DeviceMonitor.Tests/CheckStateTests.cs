using DeviceMonitor.Abstractions.Checks;
using DeviceMonitor.Abstractions.Configuration;

namespace DeviceMonitor.Tests;

public class CheckStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CheckState CpuAbove80(int failuresBeforeAlarm = 1) => new("dev1", new CheckDefinition
    {
        Metric = "snmp.cpu_load_percent",
        AlarmWhen = Comparison.GreaterThan,
        Threshold = 80,
        FailuresBeforeAlarm = failuresBeforeAlarm,
    });

    [Fact]
    public void Good_reading_sets_ok_without_event()
    {
        var check = CpuAbove80();

        var evt = check.Evaluate(40, Now);

        Assert.Null(evt);
        Assert.Equal(CheckStatus.Ok, check.Status);
    }

    [Fact]
    public void Bad_reading_raises_alarm_once()
    {
        var check = CpuAbove80();

        var first = check.Evaluate(95, Now);
        var second = check.Evaluate(97, Now.AddSeconds(10));

        Assert.NotNull(first);
        Assert.Equal(AlarmKind.Raised, first.Kind);
        Assert.Equal(95, first.Value);
        Assert.Null(second);
        Assert.Equal(CheckStatus.Alarm, check.Status);
    }

    [Fact]
    public void Alarm_waits_for_consecutive_failures()
    {
        var check = CpuAbove80(failuresBeforeAlarm: 3);

        Assert.Null(check.Evaluate(90, Now));
        Assert.Null(check.Evaluate(90, Now));
        Assert.Equal(CheckStatus.Ok, check.Status);
        Assert.Equal(2, check.ConsecutiveFailures);

        var evt = check.Evaluate(90, Now);

        Assert.Equal(AlarmKind.Raised, evt?.Kind);
        Assert.Equal(CheckStatus.Alarm, check.Status);
    }

    [Fact]
    public void Good_reading_resets_failure_counter()
    {
        var check = CpuAbove80(failuresBeforeAlarm: 2);

        check.Evaluate(90, Now);
        check.Evaluate(10, Now);
        var evt = check.Evaluate(90, Now);

        Assert.Null(evt);
        Assert.Equal(CheckStatus.Ok, check.Status);
    }

    [Fact]
    public void Recovery_emits_cleared_event()
    {
        var check = CpuAbove80();
        check.Evaluate(95, Now);

        var evt = check.Evaluate(20, Now.AddMinutes(1));

        Assert.Equal(AlarmKind.Cleared, evt?.Kind);
        Assert.Equal(CheckStatus.Ok, check.Status);
        Assert.Equal(Now.AddMinutes(1), check.StatusSince);
    }

    [Theory]
    [InlineData(Comparison.GreaterThan, 10, 10, false)]
    [InlineData(Comparison.GreaterOrEqual, 10, 10, true)]
    [InlineData(Comparison.LessThan, 9, 10, true)]
    [InlineData(Comparison.LessOrEqual, 11, 10, false)]
    [InlineData(Comparison.Equal, 0, 0, true)]
    [InlineData(Comparison.NotEqual, 1, 0, true)]
    public void Comparison_matches(Comparison comparison, double value, double threshold, bool expected) =>
        Assert.Equal(expected, comparison.Matches(value, threshold));
}
