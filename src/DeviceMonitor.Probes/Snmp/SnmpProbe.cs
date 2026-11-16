using System.Net;
using System.Net.Sockets;
using System.Text;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace DeviceMonitor.Probes.Snmp;

/// <summary>
/// Device health over SNMP v1/v2c using standard MIBs, so it works on most switches, routers, firewalls and servers.
/// What is collected is chosen with the <c>collect</c> setting (comma separated, default <c>system,cpu,memory,storage,interfaces</c>):
/// <list type="table">
/// <item><term>system</term><description><c>responding</c>, <c>uptime_s</c> (SNMPv2-MIB). Always collected.</description></item>
/// <item><term>cpu</term><description><c>cpu_load_percent</c> (average), <c>cpu_load_max_percent</c>, <c>cpu_count</c> (HOST-RESOURCES-MIB).</description></item>
/// <item><term>memory</term><description><c>memory_used_percent</c>, <c>memory_total_mb</c> (hrStorage RAM).</description></item>
/// <item><term>storage</term><description><c>disk_max_used_percent</c>, <c>disk_count</c> (hrStorage fixed disks).</description></item>
/// <item><term>interfaces</term><description><c>if_count</c>, <c>if_up</c>, <c>if_down</c> (admin up but oper down) from IF-MIB;
/// for each interface listed in the <c>interfaces</c> setting also <c>if_&lt;name&gt;.oper_up</c>, <c>.in_mbps</c>, <c>.out_mbps</c>, <c>.errors</c>.</description></item>
/// </list>
/// Any setting named <c>oid.&lt;metric&gt;</c> adds a custom numeric OID, e.g. <c>"oid.temperature_c": "1.3.6.1.4.1.9.9.13.1.3.1.3.1"</c>.
/// </summary>
/// <remarks>
/// Connection settings: <c>port</c> (161), <c>version</c> (<c>v2c</c> | <c>v1</c>), <c>timeoutMs</c> (3000) and the community, either
/// <c>communityEnv</c> (name of an environment variable holding it: keep real communities out of config files) or
/// <c>community</c> (default <c>public</c>, for labs and the bundled simulator).
/// </remarks>
public sealed class SnmpProbe : IProbe
{
    private const string DefaultSections = "system,cpu,memory,storage,interfaces";

    private readonly CounterRateTracker _counters;

    public SnmpProbe()
        : this(TimeProvider.System)
    {
    }

    internal SnmpProbe(TimeProvider timeProvider) => _counters = new CounterRateTracker(timeProvider);

    public string Type => "snmp";

    public async Task<ProbeResult> CollectAsync(DeviceDefinition device, ProbeDefinition probe, CancellationToken cancellationToken)
    {
        if (!TryGetCommunity(probe, out var community, out var communityError))
        {
            return ProbeResult.Failed(communityError);
        }

        var version = probe.GetString("version", "v2c").Equals("v1", StringComparison.OrdinalIgnoreCase) ? VersionCode.V1 : VersionCode.V2;
        var sections = probe.GetString("collect", DefaultSections)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(probe.GetInt("timeoutMs", 3000));

        try
        {
            var endpoint = await ResolveEndpointAsync(device.Host, probe.GetInt("port", 161), timeoutCts.Token);
            var session = new SnmpSession(version, endpoint, community, timeoutCts.Token);
            var metrics = new List<Metric>();

            var uptime = await session.GetAsync([SnmpOids.SysUpTime]);
            metrics.Add(Metric.Bool("responding", true));
            if (SnmpSession.ToDouble(uptime[0].Data) is { } seconds)
            {
                metrics.Add(new Metric("uptime_s", seconds, "s"));
            }

            if (sections.Contains("cpu"))
            {
                await CollectCpuAsync(session, metrics);
            }

            if (sections.Contains("memory") || sections.Contains("storage"))
            {
                await CollectStorageAsync(session, metrics, sections.Contains("memory"), sections.Contains("storage"));
            }

            if (sections.Contains("interfaces"))
            {
                await CollectInterfacesAsync(session, device, probe, metrics);
            }

            await CollectCustomOidsAsync(session, probe, metrics);

            return new ProbeResult(true, metrics);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeResult.Failed("SNMP timeout (wrong community, ACL or agent not running?)", Metric.Bool("responding", false));
        }
        catch (Exception ex) when (ex is SocketException or SnmpException)
        {
            return ProbeResult.Failed($"SNMP error: {ex.Message}", Metric.Bool("responding", false));
        }
    }

    private static async Task CollectCpuAsync(SnmpSession session, List<Metric> metrics)
    {
        var loads = (await session.WalkColumnAsync(SnmpOids.HrProcessorLoad)).Values
            .Select(SnmpSession.ToDouble).OfType<double>().ToList();

        if (loads.Count > 0)
        {
            metrics.Add(new Metric("cpu_load_percent", loads.Average(), "%"));
            metrics.Add(new Metric("cpu_load_max_percent", loads.Max(), "%"));
            metrics.Add(new Metric("cpu_count", loads.Count, "count"));
        }
    }

    private static async Task CollectStorageAsync(SnmpSession session, List<Metric> metrics, bool memory, bool storage)
    {
        var types = await session.WalkColumnAsync(SnmpOids.HrStorageType);
        if (types.Count == 0)
        {
            return;
        }

        var units = await session.WalkColumnAsync(SnmpOids.HrStorageAllocationUnits);
        var sizes = await session.WalkColumnAsync(SnmpOids.HrStorageSize);
        var used = await session.WalkColumnAsync(SnmpOids.HrStorageUsed);

        var entries = types
            .Where(t => sizes.ContainsKey(t.Key) && used.ContainsKey(t.Key) && units.ContainsKey(t.Key))
            .Select(t => (
                Type: t.Value as ObjectIdentifier,
                SizeBytes: SnmpSession.ToUnsigned(sizes[t.Key]) * SnmpSession.ToUnsigned(units[t.Key]),
                UsedBytes: SnmpSession.ToUnsigned(used[t.Key]) * SnmpSession.ToUnsigned(units[t.Key])))
            .Where(e => e.SizeBytes > 0)
            .ToList();

        var ram = entries.FirstOrDefault(e => e.Type == SnmpOids.HrStorageRam);
        if (memory && ram.SizeBytes > 0)
        {
            metrics.Add(new Metric("memory_used_percent", Math.Round(100 * ram.UsedBytes / ram.SizeBytes, 2), "%"));
            metrics.Add(new Metric("memory_total_mb", Math.Round(ram.SizeBytes / 1024 / 1024), "MB"));
        }

        var disks = entries.Where(e => e.Type == SnmpOids.HrStorageFixedDisk).ToList();
        if (storage && disks.Count > 0)
        {
            metrics.Add(new Metric("disk_max_used_percent", Math.Round(disks.Max(d => 100 * d.UsedBytes / d.SizeBytes), 2), "%"));
            metrics.Add(new Metric("disk_count", disks.Count, "count"));
        }
    }

    private async Task CollectInterfacesAsync(SnmpSession session, DeviceDefinition device, ProbeDefinition probe, List<Metric> metrics)
    {
        var admin = await session.WalkColumnAsync(SnmpOids.IfAdminStatus);
        var oper = await session.WalkColumnAsync(SnmpOids.IfOperStatus);
        if (oper.Count == 0)
        {
            return;
        }

        bool IsUp(Dictionary<uint, ISnmpData> column, uint index) =>
            column.TryGetValue(index, out var v) && SnmpSession.ToDouble(v) is SnmpOids.StatusUp;

        metrics.Add(new Metric("if_count", oper.Count, "count"));
        metrics.Add(new Metric("if_up", oper.Keys.Count(i => IsUp(oper, i)), "count"));

        // "Down" means somebody expects it up (admin up) but the link is not: disabled ports are not faults.
        metrics.Add(new Metric("if_down", oper.Keys.Count(i => IsUp(admin, i) && !IsUp(oper, i)), "count"));

        var wanted = probe.GetString("interfaces", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (wanted.Length == 0)
        {
            return;
        }

        var names = await session.WalkColumnAsync(SnmpOids.IfName);
        if (names.Count == 0)
        {
            names = await session.WalkColumnAsync(SnmpOids.IfDescr);
        }

        var selected = names
            .Where(n => wanted.Contains("*") || wanted.Contains(n.Value.ToString(), StringComparer.OrdinalIgnoreCase))
            .ToDictionary(n => n.Key, n => n.Value.ToString());
        if (selected.Count == 0)
        {
            return;
        }

        // Prefer 64 bit counters: 32 bit octet counters wrap in ~34 s on a saturated 1 Gb/s link.
        var inOctets = await session.WalkColumnAsync(SnmpOids.IfHcInOctets);
        var outOctets = await session.WalkColumnAsync(SnmpOids.IfHcOutOctets);
        var is64Bit = inOctets.Count > 0;
        if (!is64Bit)
        {
            inOctets = await session.WalkColumnAsync(SnmpOids.IfInOctets);
            outOctets = await session.WalkColumnAsync(SnmpOids.IfOutOctets);
        }

        var inErrors = await session.WalkColumnAsync(SnmpOids.IfInErrors);
        var outErrors = await session.WalkColumnAsync(SnmpOids.IfOutErrors);

        foreach (var (index, name) in selected)
        {
            var prefix = "if_" + Sanitize(name);
            var key = $"{device.Id}|{probe.MetricPrefix}|{index}";

            metrics.Add(Metric.Bool($"{prefix}.oper_up", IsUp(oper, index)));

            if (Counter(inOctets, index) is { } rx && _counters.Rate(key + "|in", rx, is64Bit) is { } rxRate)
            {
                metrics.Add(new Metric($"{prefix}.in_mbps", Math.Round(rxRate * 8 / 1_000_000, 3), "Mbps"));
            }

            if (Counter(outOctets, index) is { } tx && _counters.Rate(key + "|out", tx, is64Bit) is { } txRate)
            {
                metrics.Add(new Metric($"{prefix}.out_mbps", Math.Round(txRate * 8 / 1_000_000, 3), "Mbps"));
            }

            var errors = (Counter(inErrors, index) ?? 0) + (Counter(outErrors, index) ?? 0);
            if (_counters.Delta(key + "|errors", errors) is { } newErrors)
            {
                metrics.Add(new Metric($"{prefix}.errors", newErrors, "count"));
            }
        }
    }

    private static async Task CollectCustomOidsAsync(SnmpSession session, ProbeDefinition probe, List<Metric> metrics)
    {
        const string prefix = "oid.";
        var custom = probe.Settings
            .Where(s => s.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(s => new ObjectIdentifier(s.Value), s => s.Key[prefix.Length..]);
        if (custom.Count == 0)
        {
            return;
        }

        foreach (var variable in await session.GetAsync(custom.Keys))
        {
            // Unsupported OIDs come back as NoSuchObject/NoSuchInstance and are skipped.
            if (custom.TryGetValue(variable.Id, out var name) && SnmpSession.ToDouble(variable.Data) is { } value)
            {
                metrics.Add(new Metric(name, value, string.Empty));
            }
        }
    }

    private static ulong? Counter(Dictionary<uint, ISnmpData> column, uint index) =>
        column.TryGetValue(index, out var data) ? data switch
        {
            Counter64 c => c.ToUInt64(),
            Counter32 c => c.ToUInt32(),
            _ => null,
        } : null;

    /// <summary>"GigabitEthernet0/1" becomes "gigabitethernet0_1", safe to use in a metric name.</summary>
    internal static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString().Trim('_');
    }

    private static bool TryGetCommunity(ProbeDefinition probe, out OctetString community, out string error)
    {
        error = string.Empty;
        var variable = probe.GetString("communityEnv", string.Empty);
        if (variable.Length == 0)
        {
            community = new OctetString(probe.GetString("community", "public"));
            return true;
        }

        var value = Environment.GetEnvironmentVariable(variable);
        community = new OctetString(value ?? string.Empty);
        if (string.IsNullOrEmpty(value))
        {
            error = $"Environment variable '{variable}' with the SNMP community is not set.";
            return false;
        }

        return true;
    }

    private static async Task<IPEndPoint> ResolveEndpointAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return new IPEndPoint(address, port);
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.First();
        return new IPEndPoint(ipv4, port);
    }
}
