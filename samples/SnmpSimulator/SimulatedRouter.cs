using Lextm.SharpSnmpLib;

namespace SnmpSimulator;

/// <summary>
/// Populates a simulator with a small "router": 2 CPUs, RAM and one disk, 4 interfaces with
/// moving traffic counters, plus a vendor-style temperature sensor for the custom OID feature.
/// </summary>
public static class SimulatedRouter
{
    public const string TemperatureOid = "1.3.6.1.4.1.99999.1.1.0";

    private static readonly string[] InterfaceNames = ["GigabitEthernet0/0", "GigabitEthernet0/1", "GigabitEthernet0/2", "Loopback0"];

    public static void Populate(SnmpAgentSimulator agent)
    {
        agent.Set("1.3.6.1.2.1.1.1.0", new OctetString("DeviceMonitor SNMP simulator"));
        agent.Set("1.3.6.1.2.1.1.3.0", new TimeTicks(0));
        agent.Set("1.3.6.1.2.1.1.5.0", new OctetString("lab-router-01"));

        // hrProcessorLoad
        agent.Set("1.3.6.1.2.1.25.3.3.1.2.1", new Integer32(20));
        agent.Set("1.3.6.1.2.1.25.3.3.1.2.2", new Integer32(30));

        // hrStorage: 1 = RAM (4 GiB, 1 KiB units), 2 = fixed disk (100 GiB, 4 KiB units)
        AddStorage(agent, 1, "1.3.6.1.2.1.25.2.1.2", "Physical memory", 1024, 4 * 1024 * 1024, 2 * 1024 * 1024);
        AddStorage(agent, 2, "1.3.6.1.2.1.25.2.1.4", "/", 4096, 25 * 1024 * 1024, 15 * 1024 * 1024);

        for (var i = 0; i < InterfaceNames.Length; i++)
        {
            var index = i + 1;
            agent.Set($"1.3.6.1.2.1.2.2.1.2.{index}", new OctetString(InterfaceNames[i]));
            agent.Set($"1.3.6.1.2.1.31.1.1.1.1.{index}", new OctetString(InterfaceNames[i]));

            // Gi0/2 is administratively up but has no link: it must show up in if_down.
            agent.Set($"1.3.6.1.2.1.2.2.1.7.{index}", new Integer32(1));
            agent.Set($"1.3.6.1.2.1.2.2.1.8.{index}", new Integer32(index == 3 ? 2 : 1));
            agent.Set($"1.3.6.1.2.1.31.1.1.1.6.{index}", new Counter64(0));
            agent.Set($"1.3.6.1.2.1.31.1.1.1.10.{index}", new Counter64(0));
            agent.Set($"1.3.6.1.2.1.2.2.1.14.{index}", new Counter32(0));
            agent.Set($"1.3.6.1.2.1.2.2.1.20.{index}", new Counter32(0));
        }

        agent.Set(TemperatureOid, new Integer32(41));
    }

    /// <summary>Advances counters and gauges as if <paramref name="elapsed"/> had passed on a busy router.</summary>
    public static void Tick(SnmpAgentSimulator agent, TimeSpan elapsed, DateTimeOffset startedAt)
    {
        agent.Set("1.3.6.1.2.1.1.3.0", new TimeTicks((uint)((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds / 10)));

        var spike = Random.Shared.Next(100) < 15;
        agent.Set("1.3.6.1.2.1.25.3.3.1.2.1", new Integer32(spike ? Random.Shared.Next(85, 100) : Random.Shared.Next(10, 40)));
        agent.Set("1.3.6.1.2.1.25.3.3.1.2.2", new Integer32(Random.Shared.Next(10, 40)));
        agent.Set(TemperatureOid, new Integer32(Random.Shared.Next(38, spike ? 80 : 50)));

        // Gi0/0 ~ 40-120 Mbps in, Gi0/1 ~ 5-20 Mbps; Gi0/2 has no link so it does not move.
        Advance(agent, "1.3.6.1.2.1.31.1.1.1.6.1", Random.Shared.Next(40, 120), elapsed);
        Advance(agent, "1.3.6.1.2.1.31.1.1.1.10.1", Random.Shared.Next(10, 30), elapsed);
        Advance(agent, "1.3.6.1.2.1.31.1.1.1.6.2", Random.Shared.Next(5, 20), elapsed);
        Advance(agent, "1.3.6.1.2.1.31.1.1.1.10.2", Random.Shared.Next(5, 20), elapsed);

        if (Random.Shared.Next(100) < 10)
        {
            var errors = (Counter32)agent.Get("1.3.6.1.2.1.2.2.1.14.1")!;
            agent.Set("1.3.6.1.2.1.2.2.1.14.1", new Counter32(errors.ToUInt32() + (uint)Random.Shared.Next(1, 50)));
        }
    }

    private static void Advance(SnmpAgentSimulator agent, string oid, int mbps, TimeSpan elapsed)
    {
        var current = ((Counter64)agent.Get(oid)!).ToUInt64();
        var bytes = (ulong)(mbps * 1_000_000 / 8 * elapsed.TotalSeconds);
        agent.Set(oid, new Counter64(current + bytes));
    }

    private static void AddStorage(SnmpAgentSimulator agent, int index, string type, string description, int unitBytes, int size, int used)
    {
        agent.Set($"1.3.6.1.2.1.25.2.3.1.2.{index}", new ObjectIdentifier(type));
        agent.Set($"1.3.6.1.2.1.25.2.3.1.3.{index}", new OctetString(description));
        agent.Set($"1.3.6.1.2.1.25.2.3.1.4.{index}", new Integer32(unitBytes));
        agent.Set($"1.3.6.1.2.1.25.2.3.1.5.{index}", new Integer32(size));
        agent.Set($"1.3.6.1.2.1.25.2.3.1.6.{index}", new Integer32(used));
    }
}
