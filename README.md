# DeviceMonitor

A small, readable **device monitoring agent in .NET 10**: it polls devices with pluggable probes
(ICMP, TCP, SNMP, HTTP…), evaluates threshold checks with an OK/ALARM state machine, and
exposes the live state through a REST API and a tiny web dashboard.

It is meant as a **reference example** of how a monitoring engine is put together, not as a
replacement for Zabbix, PRTG & co. Everything fits in a few hundred lines you can read in one sitting.

```
            ┌───────────────────────────── DeviceMonitor.Agent ─────────────────────────────┐
 appsettings│  PollingService (one loop per device)                                         │
  .json ───►│    ├─ probes in parallel ──► IProbe: icmp · tcp · snmp · simulated · plugins/  │
            │    ├─ metrics "<probe>.<metric>"                                              │
            │    ├─ CheckState.Evaluate()  OK ⇄ ALARM  (N consecutive failures)            │
            │    └─ fan-out ─────────────► IResultSink: log · JSON-lines file · (your DB)   │
            │  MonitorStore ──► /api/devices · /api/alarms · /health · dashboard (/)        │
            └───────────────────────────────────────────────────────────────────────────────┘
```

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# terminal 1 – optional SNMP simulator (a fake router on 127.0.0.1:16161)
dotnet run --project samples/SnmpSimulator

# terminal 2 – the agent
dotnet run --project src/DeviceMonitor.Agent
```

Open <http://localhost:5080> for the dashboard. The default configuration monitors:

| Device | Probes | Shows |
|---|---|---|
| `localhost` | icmp, tcp, http (plugin) | basic reachability and service checks |
| `public-dns` | icmp, tcp | latency / packet loss on a real internet host |
| `lab-router-01` | snmp | CPU, RAM, disk, interface status and throughput, custom OID; one port is down on purpose |
| `lab-switch-01` | simulated | random CPU spikes that raise and clear alarms |

Samples and alarm events are also appended to `data/samples-yyyyMMdd.jsonl` and `data/alarms.jsonl`
next to the agent binary.

## Concepts

| Concept | Type | What it does |
|---|---|---|
| **Device** | `DeviceDefinition` | host + polling interval + probes + checks |
| **Probe** | `IProbe` | collects numeric metrics from a device; failures are results, not exceptions |
| **Metric** | `Metric` | `name`, `value`, `unit`; booleans are 1/0. Full name is `<probe name>.<metric>` |
| **Check** | `CheckDefinition` + `CheckState` | `AlarmWhen` `Threshold`; alarm after `FailuresBeforeAlarm` consecutive bad readings, cleared by the first good one |
| **Sink** | `IResultSink` | receives every sample and every alarm transition (log, file, database, queue…) |

A check only emits an event when its state **changes** (raised / cleared), so sinks are not
flooded with repeated alarms. If a probe fails, the checks on its metrics are left untouched:
the reachability check is what alarms on a dead device, not every other check at once.

## Probes

| Type | Metrics | Settings |
|---|---|---|
| `icmp` | `reachable`, `rtt_ms`, `packet_loss_percent` | `count` (3), `timeoutMs` (1000) |
| `tcp` | `open`, `connect_ms` | `port` (required), `timeoutMs` (2000) |
| `snmp` | see below | see below |
| `http` *(plugin)* | `up`, `status_code`, `response_ms` | `url` (`http://{host}/`), `timeoutMs` (5000) |
| `simulated` | `cpu_load_percent`, `temperature_c` | `baseCpu` (30), `spikePercent` (15) |

### SNMP

SNMP v1/v2c with standard MIBs only, so it works on most switches, routers, firewalls, servers and UPSs.
Choose what to collect with `collect` (default `system,cpu,memory,storage,interfaces`):

| Section | MIB | Metrics |
|---|---|---|
| `system` (always) | SNMPv2-MIB | `responding`, `uptime_s` |
| `cpu` | HOST-RESOURCES-MIB `hrProcessorLoad` | `cpu_load_percent` (avg), `cpu_load_max_percent`, `cpu_count` |
| `memory` | HOST-RESOURCES-MIB `hrStorage` (RAM) | `memory_used_percent`, `memory_total_mb` |
| `storage` | HOST-RESOURCES-MIB `hrStorage` (fixed disks) | `disk_max_used_percent`, `disk_count` |
| `interfaces` | IF-MIB | `if_count`, `if_up`, `if_down` (admin up **and** oper down) |

For the interfaces listed in the `interfaces` setting (by `ifName`/`ifDescr`, comma separated, `*` for all)
the probe also reports `if_<name>.oper_up`, `.in_mbps`, `.out_mbps` and `.errors` (new errors since the
previous poll). Throughput uses 64 bit counters (`ifHCInOctets`) when available and falls back to
32 bit ones, handling counter wrap and device reboots. Rates appear from the second poll on.

**Custom OIDs**: any setting named `oid.<metric>` adds a numeric OID, e.g. a vendor temperature sensor:

```json
{ "Type": "snmp", "Settings": { "oid.temperature_c": "1.3.6.1.4.1.9.9.13.1.3.1.3.1" } }
```

**Community string**: never put a real community in `appsettings.json`. Use `communityEnv` to name the
environment variable that holds it; `community` (default `public`) is only meant for labs and the simulator.

```json
{ "Type": "snmp", "Settings": { "communityEnv": "CORE_SWITCH_SNMP_COMMUNITY", "interfaces": "Gi1/0/1,Gi1/0/2" } }
```

```bash
export CORE_SWITCH_SNMP_COMMUNITY='...'          # Linux / macOS
setx CORE_SWITCH_SNMP_COMMUNITY "..."            # Windows
```

The same applies to any other setting: ASP.NET Core configuration can be overridden with environment
variables (`Monitoring__Devices__0__Host=10.0.0.1`) or [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
during development.

## Configuration example

```json
{
  "Id": "core-switch",
  "Name": "Core switch",
  "Host": "10.0.0.2",
  "IntervalSeconds": 60,
  "Probes": [
    { "Type": "icmp" },
    { "Type": "snmp", "Settings": { "communityEnv": "CORE_SWITCH_SNMP_COMMUNITY", "interfaces": "Gi1/0/48" } },
    { "Type": "tcp", "Name": "ssh", "Settings": { "port": "22" } }
  ],
  "Checks": [
    { "Metric": "icmp.reachable", "AlarmWhen": "Equal", "Threshold": 0, "FailuresBeforeAlarm": 2, "Severity": "Critical" },
    { "Metric": "snmp.cpu_load_max_percent", "AlarmWhen": "GreaterThan", "Threshold": 85, "FailuresBeforeAlarm": 3 },
    { "Metric": "snmp.if_down", "AlarmWhen": "GreaterThan", "Threshold": 0 },
    { "Metric": "snmp.if_gi1_0_48.in_mbps", "AlarmWhen": "GreaterThan", "Threshold": 800, "FailuresBeforeAlarm": 3 },
    { "Metric": "ssh.open", "AlarmWhen": "Equal", "Threshold": 0 }
  ]
}
```

`AlarmWhen`: `GreaterThan`, `GreaterOrEqual`, `LessThan`, `LessOrEqual`, `Equal`, `NotEqual`.
`Severity`: `Info`, `Warning`, `Critical`. The configuration is validated at startup and every
mistake (unknown probe, duplicate id, check on a metric no probe produces…) is reported at once.

## API

| Endpoint | Description |
|---|---|
| `GET /api/devices` | all devices with status (`Unknown`, `Ok`, `Degraded`, `Alarm`) and last metrics |
| `GET /api/devices/{id}` | one device with probe outcomes and every check state |
| `GET /api/alarms` | alarm history (raised/cleared), newest first |
| `GET /api/alarms?activeOnly=true` | checks currently in alarm |
| `GET /health` | agent liveness |

## Writing a probe plugin

Plugins are loaded at startup from `plugins/<Name>/<Name>.dll`, each in its own `AssemblyLoadContext`
so it can ship its own dependencies. [`samples/HttpProbePlugin`](samples/HttpProbePlugin) is a complete example:

1. Create a class library referencing `DeviceMonitor.Abstractions` with `Private=false` and
   `ExcludeAssets=runtime` (the host provides the contract; a second copy would break type identity).
2. Implement `IProbe` with a public parameterless constructor.
3. Copy the build output to `plugins/<AssemblyName>/` next to the agent.

```csharp
public sealed class MyProbe : IProbe
{
    public string Type => "my-probe";

    public async Task<ProbeResult> CollectAsync(DeviceDefinition device, ProbeDefinition probe, CancellationToken ct)
    {
        var value = await ReadSomethingAsync(device.Host, ct);
        return ProbeResult.Ok(new Metric("my_value", value, "unit"));
    }
}
```

Adding a sink (database, MQTT, Prometheus push…) is the same idea: implement `IResultSink` and register it in `Program.cs`.

## Running as a service

The agent uses `UseWindowsService()` / `UseSystemd()`, so the published binary can be installed as a
Windows Service (`sc.exe create DeviceMonitor binPath= "...\DeviceMonitor.Agent.exe"`) or a systemd unit
without code changes. On Linux, ICMP without root falls back to the system `ping` utility.

## Project layout

```
src/
  DeviceMonitor.Abstractions/   contracts: IProbe, IResultSink, configuration, CheckState
  DeviceMonitor.Probes/         built-in probes (icmp, tcp, snmp, simulated)
  DeviceMonitor.Agent/          host: polling loop, plugin loader, sinks, API, dashboard
samples/
  HttpProbePlugin/              external probe loaded from plugins/
  SnmpSimulator/                minimal SNMP v1/v2c agent with a fake router MIB
tests/
  DeviceMonitor.Tests/          xUnit: check state machine, polling, config validation, SNMP over UDP
```

```bash
dotnet test
```

## Ideas for extensions

SNMPv3 (authPriv), SNMP traps, persisting check state across restarts, a database sink,
notifications (mail / Teams / Slack) on alarm transitions, per-check maintenance windows.

## License

[MIT](LICENSE)
