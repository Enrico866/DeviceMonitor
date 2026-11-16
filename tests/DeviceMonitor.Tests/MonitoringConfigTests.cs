using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Agent.Configuration;

namespace DeviceMonitor.Tests;

public class MonitoringConfigTests
{
    private static readonly string[] KnownProbes = ["icmp", "tcp"];

    [Fact]
    public void Valid_configuration_passes()
    {
        var config = new MonitoringConfig
        {
            Devices =
            [
                new DeviceDefinition
                {
                    Id = "a",
                    Host = "10.0.0.1",
                    Probes = [new ProbeDefinition { Type = "icmp" }, new ProbeDefinition { Type = "tcp", Name = "ssh" }],
                    Checks = [new CheckDefinition { Metric = "ssh.open" }],
                },
            ],
        };

        config.Validate(KnownProbes);
    }

    [Fact]
    public void Reports_every_error_at_once()
    {
        var config = new MonitoringConfig
        {
            Devices =
            [
                new DeviceDefinition { Id = "a", Host = "h", Probes = [new ProbeDefinition { Type = "nope" }] },
                new DeviceDefinition { Id = "a", Host = "", Checks = [new CheckDefinition { Metric = "icmp.reachable" }] },
            ],
        };

        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate(KnownProbes));

        Assert.Contains("Duplicate device id 'a'", ex.Message);
        Assert.Contains("unknown probe type 'nope'", ex.Message);
        Assert.Contains("Host is required", ex.Message);
        Assert.Contains("does not match any probe", ex.Message);
    }
}
