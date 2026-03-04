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
    private readonly Histogram<long> _queueBacklog = Meter.CreateHistogram<long>("tcp_queue_backlog");
    private readonly Histogram<double> _persistLatencyMs = Meter.CreateHistogram<double>("tcp_persist_latency_ms");
    private readonly Histogram<long> _rawQueueBacklog = Meter.CreateHistogram<long>("tcp_raw_queue_backlog");
    private readonly Counter<long> _rawQueueDrops = Meter.CreateCounter<long>("tcp_raw_queue_drops_total");
    private readonly Counter<long> _dedupeDrops = Meter.CreateCounter<long>("tcp_dedupe_drops_total");
    private readonly Counter<long> _eventPublishFailures = Meter.CreateCounter<long>("tcp_event_publish_fail_total");
    private readonly Counter<long> _eventPublishSuccess = Meter.CreateCounter<long>("tcp_event_publish_ok_total");
    private readonly Counter<long> _eventPublishRetry = Meter.CreateCounter<long>("tcp_event_publish_retry_total");
    private readonly Counter<long> _eventDlq = Meter.CreateCounter<long>("tcp_event_dlq_total");
    private readonly Histogram<double> _eventPublishLatencyMs = Meter.CreateHistogram<double>("tcp_event_publish_latency_ms");

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

    /// <inheritdoc />
    public void RecordQueueBacklog(int port, ProtocolId protocol, long backlog)
    {
        _queueBacklog.Record(backlog, Tags(port, protocol));
    }

    /// <inheritdoc />
    public void RecordPersistLatency(int port, ProtocolId protocol, double latencyMs)
    {
        _persistLatencyMs.Record(latencyMs, Tags(port, protocol));
    }

    /// <inheritdoc />
    public void RecordRawQueueBacklog(int port, ProtocolId protocol, long backlog)
    {
        _rawQueueBacklog.Record(backlog, Tags(port, protocol));
    }

    /// <inheritdoc />
    public void RecordRawQueueDrop(int port, ProtocolId protocol)
    {
        _rawQueueDrops.Add(1, Tags(port, protocol));
    }

    /// <inheritdoc />
    public void RecordDedupeDrop(int port, ProtocolId protocol)
    {
        _dedupeDrops.Add(1, Tags(port, protocol));
    }

    /// <inheritdoc />
    public void RecordEventPublishFailure(int port, ProtocolId protocol, string eventType)
    {
        _eventPublishFailures.Add(1,
        [
            new KeyValuePair<string, object?>("port", port),
            new KeyValuePair<string, object?>("protocol", protocol.ToString()),
            new KeyValuePair<string, object?>("eventType", eventType)
        ]);
    }

    /// <inheritdoc />
    public void RecordEventPublishSuccess(int port, ProtocolId protocol, string eventType, double latencyMs)
    {
        KeyValuePair<string, object?>[] tags =
        [
            new("port", port),
            new("protocol", protocol.ToString()),
            new("eventType", eventType)
        ];

        _eventPublishSuccess.Add(1, tags);
        _eventPublishLatencyMs.Record(latencyMs, tags);
    }

    /// <inheritdoc />
    public void RecordEventPublishRetry(int port, ProtocolId protocol, string eventType)
    {
        _eventPublishRetry.Add(1,
        [
            new("port", port),
            new("protocol", protocol.ToString()),
            new("eventType", eventType)
        ]);
    }

    /// <inheritdoc />
    public void RecordEventDlq(int port, ProtocolId protocol, string eventType)
    {
        _eventDlq.Add(1,
        [
            new("port", port),
            new("protocol", protocol.ToString()),
            new("eventType", eventType)
        ]);
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
