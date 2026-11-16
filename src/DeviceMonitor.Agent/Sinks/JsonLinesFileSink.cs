using System.Text.Json;
using System.Text.Json.Serialization;
using DeviceMonitor.Abstractions.Checks;
using DeviceMonitor.Abstractions.Configuration;
using DeviceMonitor.Abstractions.Probing;
using DeviceMonitor.Abstractions.Sinks;

namespace DeviceMonitor.Agent.Sinks;

/// <summary>
/// Appends one JSON document per line to <c>samples-yyyyMMdd.jsonl</c> and <c>alarms.jsonl</c>:
/// easy to tail, grep, or import into any log/metrics tool.
/// </summary>
public sealed class JsonLinesFileSink : IResultSink
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonLinesFileSink(string directory)
    {
        _directory = Path.GetFullPath(directory, AppContext.BaseDirectory);
        Directory.CreateDirectory(_directory);
    }

    public Task WriteSampleAsync(DeviceDefinition device, ProbeDefinition probe, ProbeResult result, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var line = new
        {
            timestamp,
            deviceId = device.Id,
            probe = probe.MetricPrefix,
            result.Success,
            result.Error,
            metrics = result.Metrics.ToDictionary(m => m.Name, m => m.Value),
        };

        return AppendAsync($"samples-{timestamp:yyyyMMdd}.jsonl", line, cancellationToken);
    }

    public Task WriteAlarmAsync(AlarmEvent alarm, CancellationToken cancellationToken) =>
        AppendAsync("alarms.jsonl", alarm, cancellationToken);

    private async Task AppendAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(Path.Combine(_directory, fileName), line, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
