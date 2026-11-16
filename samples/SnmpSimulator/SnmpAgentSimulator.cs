using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace SnmpSimulator;

/// <summary>
/// Minimal read-only SNMP v1/v2c agent answering GET and GETNEXT from an in-memory MIB.
/// Enough to exercise <c>SnmpProbe</c> (and any SNMP tool) without real hardware. Not meant for production.
/// </summary>
public sealed class SnmpAgentSimulator : IAsyncDisposable
{
    private readonly ConcurrentDictionary<ObjectIdentifier, ISnmpData> _mib = new();
    private readonly UdpClient _udp;
    private readonly string _community;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public SnmpAgentSimulator(IPEndPoint endpoint, string community = "public")
    {
        _udp = new UdpClient(endpoint);
        _community = community;
    }

    public IPEndPoint Endpoint => (IPEndPoint)_udp.Client.LocalEndPoint!;

    public void Set(string oid, ISnmpData value) => _mib[new ObjectIdentifier(oid)] = value;

    public ISnmpData? Get(string oid) => _mib.TryGetValue(new ObjectIdentifier(oid), out var value) ? value : null;

    public void Start() => _loop = Task.Run(() => ReceiveLoopAsync(_stop.Token));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _udp.Dispose();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try
            {
                packet = await _udp.ReceiveAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // e.g. ICMP port unreachable reported on Windows for a previous reply: keep serving.
                continue;
            }

            var response = TryHandle(packet.Buffer);
            if (response is not null)
            {
                await _udp.SendAsync(response, packet.RemoteEndPoint, cancellationToken);
            }
        }
    }

    private byte[]? TryHandle(byte[] buffer)
    {
        ISnmpMessage request;
        try
        {
            request = MessageFactory.ParseMessages(buffer, new UserRegistry())[0];
        }
        catch (SnmpException)
        {
            return null;
        }

        // Wrong community: real agents silently drop the request, so does the simulator.
        if (request.Version == VersionCode.V3 || request.Parameters.UserName.ToString() != _community)
        {
            return null;
        }

        var variables = request.TypeCode() switch
        {
            SnmpType.GetRequestPdu => request.Pdu().Variables.Select(v => new Variable(v.Id, Lookup(v.Id))).ToList(),
            SnmpType.GetNextRequestPdu => request.Pdu().Variables.Select(v => Next(v.Id)).ToList(),
            _ => null,
        };

        if (variables is null)
        {
            return null;
        }

        // SNMPv1 has no exception values: a missing object is reported as noSuchName on the whole PDU.
        var missingIndex = request.Version == VersionCode.V1 ? variables.FindIndex(v => v.Data is NoSuchObject or EndOfMibView) : -1;
        var error = missingIndex >= 0 ? ErrorCode.NoSuchName : ErrorCode.NoError;
        var reply = missingIndex >= 0 ? request.Pdu().Variables : variables;

        return new ResponseMessage(request.RequestId(), request.Version, request.Parameters.UserName, error, missingIndex + 1, reply).ToBytes();
    }

    private ISnmpData Lookup(ObjectIdentifier oid) =>
        _mib.TryGetValue(oid, out var value) ? value : new NoSuchObject();

    private Variable Next(ObjectIdentifier oid)
    {
        var next = _mib.Keys.Where(k => k.CompareTo(oid) > 0).Order().FirstOrDefault();
        return next is null ? new Variable(oid, new EndOfMibView()) : new Variable(next, _mib[next]);
    }
}
