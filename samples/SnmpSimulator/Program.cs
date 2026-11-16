using System.Net;
using SnmpSimulator;

// Usage: dotnet run --project samples/SnmpSimulator [port]   (default 16161, community "public")
var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 16161;

await using var agent = new SnmpAgentSimulator(new IPEndPoint(IPAddress.Loopback, port));
SimulatedRouter.Populate(agent);
agent.Start();

Console.WriteLine($"SNMP simulator listening on {agent.Endpoint} (v1/v2c, community 'public'). Ctrl+C to stop.");
Console.WriteLine("Try: snmpwalk -v2c -c public 127.0.0.1:" + port + " 1.3.6.1.2.1.2.2.1.2");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

var startedAt = DateTimeOffset.UtcNow;
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
try
{
    while (await timer.WaitForNextTickAsync(stop.Token))
    {
        SimulatedRouter.Tick(agent, TimeSpan.FromSeconds(1), startedAt);
    }
}
catch (OperationCanceledException)
{
}
