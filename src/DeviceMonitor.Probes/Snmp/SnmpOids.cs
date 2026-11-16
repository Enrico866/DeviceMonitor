using Lextm.SharpSnmpLib;

namespace DeviceMonitor.Probes.Snmp;

/// <summary>Standard MIB objects supported by almost every SNMP agent (network gear, servers, UPS, printers).</summary>
internal static class SnmpOids
{
    // SNMPv2-MIB
    public static readonly ObjectIdentifier SysUpTime = new("1.3.6.1.2.1.1.3.0");

    // HOST-RESOURCES-MIB
    public static readonly ObjectIdentifier HrProcessorLoad = new("1.3.6.1.2.1.25.3.3.1.2");
    public static readonly ObjectIdentifier HrStorageType = new("1.3.6.1.2.1.25.2.3.1.2");
    public static readonly ObjectIdentifier HrStorageAllocationUnits = new("1.3.6.1.2.1.25.2.3.1.4");
    public static readonly ObjectIdentifier HrStorageSize = new("1.3.6.1.2.1.25.2.3.1.5");
    public static readonly ObjectIdentifier HrStorageUsed = new("1.3.6.1.2.1.25.2.3.1.6");
    public static readonly ObjectIdentifier HrStorageRam = new("1.3.6.1.2.1.25.2.1.2");
    public static readonly ObjectIdentifier HrStorageFixedDisk = new("1.3.6.1.2.1.25.2.1.4");

    // IF-MIB
    public static readonly ObjectIdentifier IfDescr = new("1.3.6.1.2.1.2.2.1.2");
    public static readonly ObjectIdentifier IfAdminStatus = new("1.3.6.1.2.1.2.2.1.7");
    public static readonly ObjectIdentifier IfOperStatus = new("1.3.6.1.2.1.2.2.1.8");
    public static readonly ObjectIdentifier IfInOctets = new("1.3.6.1.2.1.2.2.1.10");
    public static readonly ObjectIdentifier IfInErrors = new("1.3.6.1.2.1.2.2.1.14");
    public static readonly ObjectIdentifier IfOutOctets = new("1.3.6.1.2.1.2.2.1.16");
    public static readonly ObjectIdentifier IfOutErrors = new("1.3.6.1.2.1.2.2.1.20");
    public static readonly ObjectIdentifier IfName = new("1.3.6.1.2.1.31.1.1.1.1");
    public static readonly ObjectIdentifier IfHcInOctets = new("1.3.6.1.2.1.31.1.1.1.6");
    public static readonly ObjectIdentifier IfHcOutOctets = new("1.3.6.1.2.1.31.1.1.1.10");

    /// <summary>ifAdminStatus / ifOperStatus value meaning "up".</summary>
    public const int StatusUp = 1;
}
