using System.Diagnostics.Metrics;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Observability;

/// <summary>
/// Publicador de metricas basicas para operacion del worker TCP.
/// </summary>
public sealed class TcpMetrics : ITcpMetrics
{
    private static readonly Meter Meter = new("ImpiTrack.Tcp");
    private readonly UpDownCounter<long> _connectionsActive = Meter.CreateUpDownCounter<long>("tcp_connections_active");
    private readonly Counter<long> _framesIn = Meter.CreateCounter<long>("tcp_frames_in_total");
    private readonly Counter<long> _framesInvalid = Meter.CreateCounter<long>("tcp_frames_invalid_total");
    private readonly Counter<long> _parseOk = Meter.CreateCounter<long>("tcp_parse_ok_total");
    private readonly Counter<long> _parseFail = Meter.CreateCounter<long>("tcp_parse_fail_total");
    private readonly Counter<long> _ackSent = Meter.CreateCounter<long>("tcp_ack_sent_total");
    private readonly Histogram<double> _ackLatencyMs = Meter.CreateHistogram<double>("tcp_ack_latency_ms");

    /// <inheritdoc />
    public void RecordConnectionOpened(int port)
    {
        _connectionsActive.Add(1, new KeyValuePair<string, object?>("port", port));
    }

    /// <inheritdoc />
    public void RecordConnectionClosed(int port)
    {
        _connectionsActive.Add(-1, new KeyValuePair<string, object?>("port", port));
    }

    /// <inheritdoc />
    public void RecordFrameReceived(int port, ProtocolId protocol)
    {
        _framesIn.Add(1, Tags(port, protocol));
    }

    /// <inheritdoc />
    public void RecordFrameInvalid(int port, ProtocolId protocol)
    {
        _framesInvalid.Add(1, Tags(port, protocol));
    }

    /// <inheritdoc />
    public void RecordParseResult(int port, ProtocolId protocol, bool success)
    {
        if (success)
        {
            _parseOk.Add(1, Tags(port, protocol));
            return;
        }

        _parseFail.Add(1, Tags(port, protocol));
    }

    /// <inheritdoc />
    public void RecordAck(int port, ProtocolId protocol, double latencyMs)
    {
        _ackSent.Add(1, Tags(port, protocol));
        _ackLatencyMs.Record(latencyMs, Tags(port, protocol));
    }

    private static KeyValuePair<string, object?>[] Tags(int port, ProtocolId protocol)
    {
        return
        [
            new("port", port),
            new("protocol", protocol.ToString())
        ];
    }
}
