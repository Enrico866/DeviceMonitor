using System.Collections.Concurrent;

namespace DeviceMonitor.Probes.Snmp;

/// <summary>
/// Turns monotonically increasing SNMP counters into per-second rates by remembering the previous
/// reading of each counter. Handles 32 and 64 bit wrap-around; a counter that went backwards by
/// more than a plausible wrap (device reboot) restarts the series instead of producing a spike.
/// </summary>
internal sealed class CounterRateTracker(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, (ulong Value, long Timestamp)> _previous = new();

    /// <returns>Units per second since the previous reading, or null on the first reading / after a reset.</returns>
    public double? Rate(string key, ulong value, bool is64Bit)
    {
        var now = timeProvider.GetTimestamp();
        var hasPrevious = _previous.TryGetValue(key, out var previous);
        _previous[key] = (value, now);

        if (!hasPrevious)
        {
            return null;
        }

        var seconds = timeProvider.GetElapsedTime(previous.Timestamp, now).TotalSeconds;
        if (seconds <= 0)
        {
            return null;
        }

        ulong delta;
        if (value >= previous.Value)
        {
            delta = value - previous.Value;
        }
        else if (!is64Bit)
        {
            delta = (uint.MaxValue - previous.Value) + value + 1;
        }
        else
        {
            // A 64 bit counter does not wrap in practice: this is a reset.
            return null;
        }

        return delta / seconds;
    }

    /// <summary>Difference since the previous reading (e.g. new errors), or null on the first reading / after a reset.</summary>
    public ulong? Delta(string key, ulong value)
    {
        var hasPrevious = _previous.TryGetValue(key, out var previous);
        _previous[key] = (value, timeProvider.GetTimestamp());
        return hasPrevious && value >= previous.Value ? value - previous.Value : null;
    }
}
