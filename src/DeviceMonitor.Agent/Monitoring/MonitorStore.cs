using System.Collections.Concurrent;
using DeviceMonitor.Abstractions.Checks;
using DeviceMonitor.Abstractions.Configuration;

namespace DeviceMonitor.Agent.Monitoring;

/// <summary>In-memory state served by the API: every device plus a bounded history of alarm events.</summary>
public sealed class MonitorStore(IEnumerable<DeviceDefinition> devices)
{
    private const int MaxAlarmHistory = 500;

    private readonly ConcurrentQueue<AlarmEvent> _alarmHistory = new();

    public IReadOnlyList<DeviceRuntime> Devices { get; } = devices.Select(d => new DeviceRuntime(d)).ToList();

    public DeviceRuntime? Find(string id) =>
        Devices.FirstOrDefault(d => string.Equals(d.Definition.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Newest first.</summary>
    public IReadOnlyList<AlarmEvent> AlarmHistory => _alarmHistory.Reverse().ToList();

    public void AddAlarm(AlarmEvent alarm)
    {
        _alarmHistory.Enqueue(alarm);
        while (_alarmHistory.Count > MaxAlarmHistory && _alarmHistory.TryDequeue(out _))
        {
        }
    }
}
