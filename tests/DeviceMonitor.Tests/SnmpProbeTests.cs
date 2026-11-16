using System.Net;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;
using DeviceMonitor.Probes.Snmp;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Time.Testing;
using SnmpSimulator;

namespace DeviceMonitor.Tests;

/// <summary>End-to-end over UDP against the bundled simulator: exercises the real SharpSnmpLib stack.</summary>
public sealed class SnmpProbeTests : IAsyncLifetime
{
    private const string Gi00InOctets = "1.3.6.1.2.1.31.1.1.1.6.1";
    private const string Gi00InErrors = "1.3.6.1.2.1.2.2.1.14.1";

    private readonly SnmpAgentSimulator _agent = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly FakeTimeProvider _time = new();

    public Task InitializeAsync()
    {
        SimulatedRouter.Populate(_agent);
        _agent.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _agent.DisposeAsync();

    [Fact]
    public async Task Collects_standard_mib_metrics_and_custom_oids()
    {
        var probe = Probe(("oid.temperature_c", SimulatedRouter.TemperatureOid), ("oid.missing", "1.3.6.1.4.1.99999.9.9.0"));

        var result = await new SnmpProbe(_time).CollectAsync(Device(), probe, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var m = result.Metrics.ToDictionary(x => x.Name, x => x.Value);
        Assert.Equal(1, m["responding"]);
        Assert.Equal(25, m["cpu_load_percent"]);
        Assert.Equal(30, m["cpu_load_max_percent"]);
        Assert.Equal(2, m["cpu_count"]);
        Assert.Equal(50, m["memory_used_percent"]);
        Assert.Equal(4096, m["memory_total_mb"]);
        Assert.Equal(60, m["disk_max_used_percent"]);
        Assert.Equal(4, m["if_count"]);
        Assert.Equal(3, m["if_up"]);
        Assert.Equal(1, m["if_down"]);
        Assert.Equal(41, m["temperature_c"]);
        Assert.False(m.ContainsKey("missing"));
    }

    [Fact]
    public async Task Interface_rates_and_errors_come_from_counter_deltas()
    {
        var snmp = new SnmpProbe(_time);
        var probe = Probe(("interfaces", "GigabitEthernet0/0, GigabitEthernet0/2"));

        var first = await snmp.CollectAsync(Device(), probe, CancellationToken.None);
        Assert.DoesNotContain(first.Metrics, x => x.Name.EndsWith("in_mbps", StringComparison.Ordinal));

        // 10 s later: 125 MB received on Gi0/0 = 100 Mbps, plus 7 new input errors.
        _agent.Set(Gi00InOctets, new Counter64(125_000_000));
        _agent.Set(Gi00InErrors, new Counter32(7));
        _time.Advance(TimeSpan.FromSeconds(10));

        var second = await snmp.CollectAsync(Device(), probe, CancellationToken.None);

        var m = second.Metrics.ToDictionary(x => x.Name, x => x.Value);
        Assert.Equal(100, m["if_gigabitethernet0_0.in_mbps"]);
        Assert.Equal(0, m["if_gigabitethernet0_0.out_mbps"]);
        Assert.Equal(7, m["if_gigabitethernet0_0.errors"]);
        Assert.Equal(1, m["if_gigabitethernet0_0.oper_up"]);
        Assert.Equal(0, m["if_gigabitethernet0_2.oper_up"]);
        Assert.DoesNotContain(second.Metrics, x => x.Name.StartsWith("if_gigabitethernet0_1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wrong_community_reports_not_responding()
    {
        var probe = Probe(("community", "wrong"), ("timeoutMs", "400"));

        var result = await new SnmpProbe(_time).CollectAsync(Device(), probe, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, Assert.Single(result.Metrics, x => x.Name == "responding").Value);
    }

    [Fact]
    public async Task Community_from_missing_environment_variable_fails_with_clear_error()
    {
        var probe = Probe(("communityEnv", "DEVICEMONITOR_TEST_UNSET_" + Guid.NewGuid().ToString("N")));

        var result = await new SnmpProbe(_time).CollectAsync(Device(), probe, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("is not set", result.Error);
    }

    [Fact]
    public void Counter32_wrap_is_handled()
    {
        var tracker = new CounterRateTracker(_time);
        tracker.Rate("k", uint.MaxValue - 99, is64Bit: false);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(200, tracker.Rate("k", 100, is64Bit: false));
    }

    [Fact]
    public void Counter64_going_backwards_is_a_reset_not_a_spike()
    {
        var tracker = new CounterRateTracker(_time);
        tracker.Rate("k", 1_000_000, is64Bit: true);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(tracker.Rate("k", 10, is64Bit: true));
    }

    [Theory]
    [InlineData("GigabitEthernet0/1", "gigabitethernet0_1")]
    [InlineData("Port-channel 10", "port_channel_10")]
    [InlineData("/eth0/", "eth0")]
    public void Interface_names_are_sanitized(string input, string expected) =>
        Assert.Equal(expected, SnmpProbe.Sanitize(input));

    private DeviceDefinition Device() => new() { Id = "router", Host = "127.0.0.1" };

    private ProbeDefinition Probe(params (string Key, string Value)[] settings)
    {
        var probe = new ProbeDefinition { Type = "snmp" };
        probe.Settings["port"] = _agent.Endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (key, value) in settings)
        {
            probe.Settings[key] = value;
        }

        return probe;
    }
}
