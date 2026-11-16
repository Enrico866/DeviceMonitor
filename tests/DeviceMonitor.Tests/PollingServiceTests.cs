using DeviceMonitor.Abstractions.Checks;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;
using DeviceMonitor.Abstractions.Sinks;
using DeviceMonitor.Agent.Monitoring;
using DeviceMonitor.Agent.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeviceMonitor.Tests;

public class PollingServiceTests
{
    [Fact]
    public async Task Poll_records_prefixed_metrics_and_raises_alarm()
    {
        var probe = new FakeProbe { Next = ProbeResult.Ok(new Metric("load", 99, "%")) };
        var sink = new RecordingSink();
        var (service, device) = Create(probe, sink, new CheckDefinition { Metric = "fake.load", AlarmWhen = Comparison.GreaterThan, Threshold = 90 });

        await service.PollOnceAsync(device, CancellationToken.None);

        Assert.Equal(99, device.Metrics["fake.load"].Value);
        Assert.Equal(DeviceStatus.Alarm, device.Status);
        Assert.Single(sink.Samples);
        Assert.Equal(AlarmKind.Raised, Assert.Single(sink.Alarms).Kind);
    }

    [Fact]
    public async Task Missing_metric_leaves_check_untouched_and_marks_device_degraded()
    {
        var probe = new FakeProbe { Next = ProbeResult.Failed("timeout") };
        var sink = new RecordingSink();
        var (service, device) = Create(probe, sink, new CheckDefinition { Metric = "fake.load", Threshold = 90 });

        await service.PollOnceAsync(device, CancellationToken.None);

        Assert.Equal(CheckStatus.Unknown, device.Checks[0].Status);
        Assert.Equal(DeviceStatus.Degraded, device.Status);
        Assert.Empty(sink.Alarms);
    }

    [Fact]
    public async Task Throwing_probe_is_reported_as_failure()
    {
        var probe = new FakeProbe { Throw = new InvalidOperationException("boom") };
        var (service, device) = Create(probe, new RecordingSink());

        await service.PollOnceAsync(device, CancellationToken.None);

        Assert.False(device.Probes["fake"].Success);
        Assert.Equal("boom", device.Probes["fake"].Error);
    }

    private static (PollingService Service, DeviceRuntime Device) Create(IProbe probe, IResultSink sink, params CheckDefinition[] checks)
    {
        var definition = new DeviceDefinition
        {
            Id = "dev1",
            Host = "test",
            Probes = [new ProbeDefinition { Type = probe.Type }],
            Checks = [.. checks],
        };
        var store = new MonitorStore([definition]);
        var service = new PollingService(store, new ProbeRegistry([probe]), [sink], TimeProvider.System, NullLogger<PollingService>.Instance);
        return (service, store.Devices[0]);
    }

    private sealed class FakeProbe : IProbe
    {
        public ProbeResult Next { get; set; } = ProbeResult.Ok();
        public Exception? Throw { get; set; }
        public string Type => "fake";

        public Task<ProbeResult> CollectAsync(DeviceDefinition device, ProbeDefinition probe, CancellationToken cancellationToken) =>
            Throw is null ? Task.FromResult(Next) : Task.FromException<ProbeResult>(Throw);
    }

    private sealed class RecordingSink : IResultSink
    {
        public List<ProbeResult> Samples { get; } = [];
        public List<AlarmEvent> Alarms { get; } = [];

        public Task WriteSampleAsync(DeviceDefinition device, ProbeDefinition probe, ProbeResult result, DateTimeOffset timestamp, CancellationToken cancellationToken)
        {
            Samples.Add(result);
            return Task.CompletedTask;
        }

        public Task WriteAlarmAsync(AlarmEvent alarm, CancellationToken cancellationToken)
        {
            Alarms.Add(alarm);
            return Task.CompletedTask;
        }
    }
}
