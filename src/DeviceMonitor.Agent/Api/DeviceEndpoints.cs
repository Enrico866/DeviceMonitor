using DeviceMonitor.Agent.Monitoring;

namespace DeviceMonitor.Agent.Api;

/// <summary>Read-only HTTP API over <see cref="MonitorStore"/>.</summary>
public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/devices", (MonitorStore store) => store.Devices.Select(ToSummary));

        api.MapGet("/devices/{id}", (string id, MonitorStore store) =>
            store.Find(id) is { } device ? Results.Ok(ToDetail(device)) : Results.NotFound());

        api.MapGet("/alarms", (MonitorStore store, bool activeOnly = false) => activeOnly
            ? store.Devices.SelectMany(d => d.Checks)
                .Where(c => c.Status == Abstractions.Checks.CheckStatus.Alarm)
                .Select(c => new { c.DeviceId, c.Definition.Metric, c.Definition.Severity, Description = c.Definition.DisplayName, c.LastValue, Since = c.StatusSince })
                .Cast<object>()
            : store.AlarmHistory);

        app.MapGet("/health", (MonitorStore store) => Results.Ok(new
        {
            status = "running",
            devices = store.Devices.Count,
            inAlarm = store.Devices.Count(d => d.Status == DeviceStatus.Alarm),
        }));
    }

    private static object ToSummary(DeviceRuntime device) => new
    {
        device.Definition.Id,
        device.Definition.Name,
        device.Definition.Host,
        device.Status,
        device.LastPollAt,
        Metrics = device.Metrics.OrderBy(m => m.Key).ToDictionary(m => m.Key, m => new { m.Value.Value, m.Value.Unit }),
        ActiveAlarms = device.Checks.Count(c => c.Status == Abstractions.Checks.CheckStatus.Alarm),
    };

    private static object ToDetail(DeviceRuntime device) => new
    {
        device.Definition.Id,
        device.Definition.Name,
        device.Definition.Host,
        device.Definition.IntervalSeconds,
        device.Status,
        device.LastPollAt,
        Probes = device.Probes,
        Metrics = device.Metrics.OrderBy(m => m.Key).ToDictionary(m => m.Key, m => m.Value),
        Checks = device.Checks.Select(c => new
        {
            c.Definition.Metric,
            Rule = $"{c.Definition.AlarmWhen} {c.Definition.Threshold}",
            c.Definition.Severity,
            c.Status,
            c.LastValue,
            c.ConsecutiveFailures,
            c.StatusSince,
        }),
    };
}
