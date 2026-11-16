using System.Globalization;
using DeviceMonitor.Abstractions.Configuration;

namespace DeviceMonitor.Abstractions.Probing;

/// <summary>Typed accessors for <see cref="ProbeDefinition.Settings"/>.</summary>
public static class ProbeSettings
{
    public static string GetString(this ProbeDefinition probe, string key, string defaultValue) =>
        probe.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : defaultValue;

    public static int GetInt(this ProbeDefinition probe, string key, int defaultValue)
    {
        if (!probe.Settings.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"Probe '{probe.MetricPrefix}': setting '{key}' must be an integer, got '{raw}'.");
    }

    public static int GetRequiredInt(this ProbeDefinition probe, string key) =>
        probe.Settings.ContainsKey(key)
            ? probe.GetInt(key, 0)
            : throw new InvalidOperationException($"Probe '{probe.MetricPrefix}': missing required setting '{key}'.");
}
