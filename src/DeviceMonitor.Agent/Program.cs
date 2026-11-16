using System.Text.Json.Serialization;
using DeviceMonitor.Abstractions.Sinks;
using DeviceMonitor.Agent.Api;
using DeviceMonitor.Agent.Configuration;
using DeviceMonitor.Agent.Monitoring;
using DeviceMonitor.Agent.Plugins;
using DeviceMonitor.Agent.Sinks;

var builder = WebApplication.CreateBuilder(args);

// Lets the same binary run as a Windows Service or a systemd unit with no code change.
builder.Host.UseWindowsService();
builder.Host.UseSystemd();

var config = builder.Configuration.GetSection(MonitoringConfig.SectionName).Get<MonitoringConfig>() ?? new MonitoringConfig();

using var startupLoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
var registry = ProbeRegistry.Load(config.PluginsPath, startupLoggerFactory.CreateLogger<ProbeRegistry>());
config.Validate(registry.Types);

builder.Services.AddSingleton(registry);
builder.Services.AddSingleton(new MonitorStore(config.Devices));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IResultSink, LoggingSink>();
if (!string.IsNullOrWhiteSpace(config.DataDirectory))
{
    builder.Services.AddSingleton<IResultSink>(new JsonLinesFileSink(config.DataDirectory));
}

builder.Services.AddHostedService<PollingService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapDeviceEndpoints();

app.Run();
