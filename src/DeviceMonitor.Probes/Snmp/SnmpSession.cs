using System.Globalization;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace DeviceMonitor.Probes.Snmp;

/// <summary>Thin wrapper over SharpSnmpLib for one polling cycle: GET, table walks and value conversion.</summary>
internal sealed class SnmpSession(VersionCode version, IPEndPoint endpoint, OctetString community, CancellationToken cancellationToken)
{
    public async Task<IList<Variable>> GetAsync(IEnumerable<ObjectIdentifier> oids) =>
        await Messenger.GetAsync(version, endpoint, community, oids.Select(o => new Variable(o)).ToList(), cancellationToken);

    /// <summary>Walks one table column and returns its values keyed by row index (the last OID sub-identifier).</summary>
    public async Task<Dictionary<uint, ISnmpData>> WalkColumnAsync(ObjectIdentifier column)
    {
        var rows = new List<Variable>();
        await Messenger.WalkAsync(version, endpoint, community, column, rows, WalkMode.WithinSubtree, cancellationToken);
        return rows.ToDictionary(v => v.Id.ToNumerical()[^1], v => v.Data);
    }

    /// <summary>Numeric value of any SNMP scalar type; null for NoSuchObject/NoSuchInstance or non-numeric strings.</summary>
    public static double? ToDouble(ISnmpData data) => data switch
    {
        Integer32 i => i.ToInt32(),
        Counter32 c => c.ToUInt32(),
        Gauge32 g => g.ToUInt32(),
        Counter64 c => c.ToUInt64(),
        TimeTicks t => t.ToTimeSpan().TotalSeconds,

        // Some vendors expose sensors as strings, e.g. "42.5".
        OctetString s when double.TryParse(s.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    /// <summary>HOST-RESOURCES sizes are Integer32 but semantically unsigned: large disks overflow to negative values.</summary>
    public static double ToUnsigned(ISnmpData data) => data is Integer32 i ? (uint)i.ToInt32() : ToDouble(data) ?? 0;
}
