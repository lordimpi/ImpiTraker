using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.IOptionPattern;
using ImpiTrack.Observability;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.Correlation;
using ImpiTrack.Tcp.Core.Framing;
using ImpiTrack.Tcp.Core.Protocols;
using ImpiTrack.Tcp.Core.Queue;
using ImpiTrack.Tcp.Core.Security;
using ImpiTrack.Tcp.Core.Sessions;
using TcpServer.RawQueue;

namespace TcpServer;

/// <summary>
/// Worker asincrono TCP multi-puerto para ingesta con framing, parseo de protocolo y despacho de ACK.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TcpServerOptions _options;
    private readonly IProtocolResolver _protocolResolver;
    private readonly IReadOnlyDictionary<ProtocolId, IProtocolParser> _parsers;
    private readonly IReadOnlyDictionary<ProtocolId, IAckStrategy> _ackStrategies;
    private readonly IInboundQueue _inboundQueue;
    private readonly IRawPacketQueue _rawPacketQueue;
    private readonly ISessionManager _sessionManager;
    private readonly IPacketIdGenerator _packetIdGenerator;
    private readonly IIngestionRepository _ingestionRepository;
    private readonly IAbuseGuard _abuseGuard;
    private readonly ITcpMetrics _tcpMetrics;

    /// <summary>
    /// Crea el worker de ingesta TCP y sus dependencias de runtime.
    /// </summary>
    /// <param name="logger">Instancia de logger.</param>
    /// <param name="options">Opciones TCP del servidor enlazadas.</param>
    /// <param name="protocolResolver">Estrategia de resolucion de protocolo.</param>
    /// <param name="parsers">Parsers de protocolo registrados.</param>
    /// <param name="ackStrategies">Estrategias ACK registradas.</param>
    /// <param name="inboundQueue">Cola entrante acotada.</param>
    /// <param name="sessionManager">Administrador de estado de sesiones.</param>
    /// <param name="packetIdGenerator">Generador de id de paquete.</param>
    /// <param name="ingestionRepository">Repositorio de persistencia de ingesta.</param>
    /// <param name="abuseGuard">Control de abuso por IP.</param>
    /// <param name="tcpMetrics">Publicador de metricas TCP.</param>
    public Worker(
        ILogger<Worker> logger,
        IGenericOptionsService<TcpServerOptions> optionsService,
        IProtocolResolver protocolResolver,
        IEnumerable<IProtocolParser> parsers,
        IEnumerable<IAckStrategy> ackStrategies,
        IInboundQueue inboundQueue,
        IRawPacketQueue rawPacketQueue,
        ISessionManager sessionManager,
        IPacketIdGenerator packetIdGenerator,
        IIngestionRepository ingestionRepository,
        IAbuseGuard abuseGuard,
        ITcpMetrics tcpMetrics)
    {
        _logger = logger;
        _options = optionsService.GetOptions();
        _protocolResolver = protocolResolver;
        _parsers = parsers.ToDictionary(x => x.Protocol);
        _ackStrategies = ackStrategies.ToDictionary(x => x.Protocol);
        _inboundQueue = inboundQueue;
        _rawPacketQueue = rawPacketQueue;
        _sessionManager = sessionManager;
        _packetIdGenerator = packetIdGenerator;
        _ingestionRepository = ingestionRepository;
        _abuseGuard = abuseGuard;
        _tcpMetrics = tcpMetrics;
    }

    /// <summary>
    /// Ejecuta listeners TCP para todos los endpoints configurados.
    /// </summary>
    /// <param name="stoppingToken">Token de cancelacion de apagado.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Servers.Count == 0)
        {
            _logger.LogWarning("no_tcp_servers_configured section={section}", TcpServerOptions.SectionName);
            return;
        }

        List<Task> listeners = [];
        foreach (TcpServerEndpointOptions endpoint in _options.Servers)
        {
            listeners.Add(RunListenerAsync(endpoint, stoppingToken));
        }

        await Task.WhenAll(listeners);
    }

    private async Task RunListenerAsync(TcpServerEndpointOptions endpoint, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Any, endpoint.Port);
        listener.Server.ReceiveBufferSize = _options.Socket.ReceiveBufferBytes;
        listener.Start();

        _logger.LogInformation(
            "listener_started name={name} port={port} configuredProtocol={protocol}",
            endpoint.Name,
            endpoint.Port,
            endpoint.Protocol);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(endpoint, client, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("listener_stopped port={port}", endpoint.Port);
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(
        TcpServerEndpointOptions endpoint,
        TcpClient client,
        CancellationToken serverToken)
    {
        string remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
        SessionState session = _sessionManager.Open(remoteIp, endpoint.Port);
        IFrameDecoder frameDecoder = CreateDecoderForEndpoint(endpoint);
        DateTimeOffset connectedAt = session.ConnectedAtUtc;
        bool firstFrameReceived = false;
        string closeReason = "remote_closed";

        _tcpMetrics.RecordConnectionOpened(endpoint.Port);
        await TryUpsertSessionAsync(
            ToSessionRecord(session, isActive: true),
            "session_open_snapshot",
            serverToken);

        _logger.LogInformation(
            "session_open sessionId={sessionId} remoteIp={remoteIp} port={port}",
            session.SessionId,
            remoteIp,
            endpoint.Port);

        try
        {
            using (client)
            {
                client.NoDelay = true;
                using NetworkStream stream = client.GetStream();
                PipeReader reader = PipeReader.Create(stream);
                bool disconnectRequested = false;

                while (!serverToken.IsCancellationRequested)
                {
                    if (_abuseGuard.IsBlocked(remoteIp, DateTimeOffset.UtcNow, out DateTimeOffset? blockedUntil))
                    {
                        closeReason = "abuse_blocked_ip";
                        _sessionManager.SetCloseReason(session.SessionId, closeReason);
                        _logger.LogWarning(
                            "session_blocked sessionId={sessionId} remoteIp={remoteIp} port={port} blockedUntil={blockedUntil}",
                            session.SessionId,
                            remoteIp,
                            endpoint.Port,
                            blockedUntil);
                        break;
                    }

                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                    TimeSpan readTimeout = TimeSpan.FromSeconds(_options.Socket.ReadTimeoutSeconds);
                    if (!firstFrameReceived)
                    {
                        TimeSpan handshakeBudget =
                            TimeSpan.FromSeconds(_options.Socket.HandshakeTimeoutSeconds) -
                            (DateTimeOffset.UtcNow - connectedAt);
                        if (handshakeBudget <= TimeSpan.Zero)
                        {
                            closeReason = "handshake_timeout";
                            _sessionManager.SetCloseReason(session.SessionId, closeReason);
                            _logger.LogWarning(
                                "session_handshake_timeout sessionId={sessionId} remoteIp={remoteIp} port={port}",
                                session.SessionId,
                                remoteIp,
                                endpoint.Port);
                            break;
                        }

                        readTimeout = handshakeBudget < readTimeout ? handshakeBudget : readTimeout;
                    }

                    if (readTimeout < TimeSpan.FromMilliseconds(200))
                    {
                        readTimeout = TimeSpan.FromMilliseconds(200);
                    }

                    readCts.CancelAfter(readTimeout);

                    ReadResult result;
                    try
                    {
                        result = await reader.ReadAsync(readCts.Token);
                    }
                    catch (OperationCanceledException) when (!serverToken.IsCancellationRequested)
                    {
                        closeReason = "read_timeout";
                        _sessionManager.SetCloseReason(session.SessionId, closeReason);
                        _logger.LogWarning(
                            "session_read_timeout sessionId={sessionId} remoteIp={remoteIp} port={port}",
                            session.SessionId,
                            remoteIp,
                            endpoint.Port);
                        break;
                    }

                    ReadOnlySequence<byte> buffer = result.Buffer;
                    while (frameDecoder.TryReadFrame(ref buffer, out Frame frame))
                    {
                        firstFrameReceived = true;
                        _sessionManager.Touch(session.SessionId);
                        _sessionManager.IncrementFramesIn(session.SessionId);

                        PacketId packetId = _packetIdGenerator.Next();
                        ProtocolId protocol = _protocolResolver.Resolve(endpoint.Port, FramePreview(frame.Payload));
                        _tcpMetrics.RecordFrameReceived(endpoint.Port, protocol);

                        if (!_parsers.TryGetValue(protocol, out IProtocolParser? parser))
                        {
                            RegisterInvalidFrame(remoteIp, endpoint.Port, protocol, session.SessionId);
                            bool rawQueuedNoParser = await TryAddRawPacketAsync(
                                new RawPacketRecord(
                                    session.SessionId,
                                    packetId,
                                    endpoint.Port,
                                    remoteIp,
                                    protocol,
                                    null,
                                    MessageType.Unknown,
                                    ReadText(frame.Payload),
                                    frame.ReceivedAtUtc,
                                    RawParseStatus.Failed,
                                    "parse_no_parser",
                                    false,
                                    null,
                                    null,
                                    null),
                                _inboundQueue.Backlog,
                                "parse_no_parser",
                                serverToken);
                            if (!rawQueuedNoParser && _rawPacketQueue.FullMode == RawQueueFullMode.Disconnect)
                            {
                                disconnectRequested = true;
                                break;
                            }

                            _logger.LogWarning(
                                "parse_skip_no_parser sessionId={sessionId} packetId={packetId} protocol={protocol} port={port}",
                                session.SessionId,
                                packetId,
                                protocol,
                                endpoint.Port);
                            continue;
                        }

                        if (!parser.TryParse(frame, out ParsedMessage? parsed, out string? parseError) || parsed is null)
                        {
                            RegisterInvalidFrame(remoteIp, endpoint.Port, protocol, session.SessionId);
                            bool rawQueuedParseFail = await TryAddRawPacketAsync(
                                new RawPacketRecord(
                                    session.SessionId,
                                    packetId,
                                    endpoint.Port,
                                    remoteIp,
                                    protocol,
                                    null,
                                    MessageType.Unknown,
                                    ReadText(frame.Payload),
                                    frame.ReceivedAtUtc,
                                    RawParseStatus.Failed,
                                    parseError ?? "unknown_parse_error",
                                    false,
                                    null,
                                    null,
                                    null),
                                _inboundQueue.Backlog,
                                "parse_fail",
                                serverToken);
                            if (!rawQueuedParseFail && _rawPacketQueue.FullMode == RawQueueFullMode.Disconnect)
                            {
                                disconnectRequested = true;
                                break;
                            }

                            _logger.LogWarning(
                                "parse_fail sessionId={sessionId} packetId={packetId} protocol={protocol} port={port} error={error}",
                                session.SessionId,
                                packetId,
                                protocol,
                                endpoint.Port,
                                parseError ?? "unknown_parse_error");
                            continue;
                        }

                        _abuseGuard.RegisterFrame(remoteIp, isInvalid: false, DateTimeOffset.UtcNow);
                        _tcpMetrics.RecordParseResult(endpoint.Port, parsed.Protocol, success: true);

                        _sessionManager.AttachImei(session.SessionId, parsed.Imei);
                        if (parsed.MessageType == MessageType.Heartbeat)
                        {
                            _sessionManager.MarkHeartbeat(session.SessionId);
                        }

                        bool ackSent = false;
                        DateTimeOffset? ackAtUtc = null;
                        double? ackLatencyMs = null;
                        string? ackPayload = null;

                        if (_ackStrategies.TryGetValue(parsed.Protocol, out IAckStrategy? ackStrategy) &&
                            ackStrategy.TryBuildAck(parsed, out ReadOnlyMemory<byte> ackBytes))
                        {
                            DateTimeOffset ackStarted = DateTimeOffset.UtcNow;
                            await stream.WriteAsync(ackBytes, serverToken);
                            ackLatencyMs = (DateTimeOffset.UtcNow - ackStarted).TotalMilliseconds;
                            ackAtUtc = DateTimeOffset.UtcNow;
                            ackPayload = Truncate(ReadText(ackBytes), 200);
                            ackSent = true;

                            _tcpMetrics.RecordAck(endpoint.Port, parsed.Protocol, ackLatencyMs.Value);
                            _logger.LogInformation(
                                "ack_sent sessionId={sessionId} packetId={packetId} protocol={protocol} messageType={messageType} imei={imei} port={port} latencyMs={latencyMs}",
                                session.SessionId,
                                packetId,
                                parsed.Protocol,
                                parsed.MessageType,
                                parsed.Imei ?? "n/a",
                                endpoint.Port,
                                ackLatencyMs);
                        }

                        bool rawQueuedParseOk = await TryAddRawPacketAsync(
                            new RawPacketRecord(
                                session.SessionId,
                                packetId,
                                endpoint.Port,
                                remoteIp,
                                parsed.Protocol,
                                parsed.Imei,
                                parsed.MessageType,
                                parsed.Text,
                                parsed.ReceivedAtUtc,
                                RawParseStatus.Ok,
                                null,
                                ackSent,
                                ackPayload,
                                ackAtUtc,
                                ackLatencyMs),
                            _inboundQueue.Backlog,
                            "parse_ok",
                            serverToken);
                        if (!rawQueuedParseOk && _rawPacketQueue.FullMode == RawQueueFullMode.Disconnect)
                        {
                            disconnectRequested = true;
                            break;
                        }

                        await _inboundQueue.EnqueueAsync(
                            new InboundEnvelope(
                                session.SessionId,
                                packetId,
                                endpoint.Port,
                                remoteIp,
                                parsed,
                                DateTimeOffset.UtcNow),
                            serverToken);
                        _tcpMetrics.RecordQueueBacklog(endpoint.Port, parsed.Protocol, _inboundQueue.Backlog);

                        _logger.LogInformation(
                            "frame_enqueued sessionId={sessionId} packetId={packetId} protocol={protocol} messageType={messageType} imei={imei} port={port} backlog={backlog}",
                            session.SessionId,
                            packetId,
                            parsed.Protocol,
                            parsed.MessageType,
                            parsed.Imei ?? "n/a",
                            endpoint.Port,
                            _inboundQueue.Backlog);

                        if (_sessionManager.TryGet(session.SessionId, out SessionState? currentSession) &&
                            currentSession is not null)
                        {
                            await TryUpsertSessionAsync(
                                ToSessionRecord(currentSession, isActive: true),
                                "session_progress_snapshot",
                                serverToken);
                        }
                    }

                    if (disconnectRequested)
                    {
                        closeReason = "raw_queue_overflow_disconnect";
                        _sessionManager.SetCloseReason(session.SessionId, closeReason);
                        _logger.LogWarning(
                            "session_disconnect_raw_queue_overflow sessionId={sessionId} remoteIp={remoteIp} port={port}",
                            session.SessionId,
                            remoteIp,
                            endpoint.Port);
                        break;
                    }

                    if (!firstFrameReceived &&
                        DateTimeOffset.UtcNow - connectedAt > TimeSpan.FromSeconds(_options.Socket.HandshakeTimeoutSeconds))
                    {
                        closeReason = "handshake_timeout";
                        _sessionManager.SetCloseReason(session.SessionId, closeReason);
                        _logger.LogWarning(
                            "session_handshake_timeout sessionId={sessionId} remoteIp={remoteIp} port={port}",
                            session.SessionId,
                            remoteIp,
                            endpoint.Port);
                        break;
                    }

                    if (_sessionManager.TryGet(session.SessionId, out SessionState? current) &&
                        current is not null &&
                        DateTimeOffset.UtcNow - current.LastSeenAtUtc > TimeSpan.FromSeconds(_options.Socket.IdleTimeoutSeconds))
                    {
                        closeReason = "idle_timeout";
                        _sessionManager.SetCloseReason(session.SessionId, closeReason);
                        _logger.LogWarning(
                            "session_idle_timeout sessionId={sessionId} remoteIp={remoteIp} port={port}",
                            session.SessionId,
                            remoteIp,
                            endpoint.Port);
                        break;
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                    {
                        closeReason = "remote_closed";
                        _sessionManager.SetCloseReason(session.SessionId, closeReason);
                        break;
                    }
                }

                await reader.CompleteAsync();
            }
        }
        catch (InvalidDataException ex)
        {
            closeReason = "frame_rejected";
            _sessionManager.SetCloseReason(session.SessionId, closeReason);
            RegisterInvalidFrame(remoteIp, endpoint.Port, ProtocolId.Unknown, session.SessionId);

            await TryAddRawPacketAsync(
                new RawPacketRecord(
                    session.SessionId,
                    _packetIdGenerator.Next(),
                    endpoint.Port,
                    remoteIp,
                    ProtocolId.Unknown,
                    null,
                    MessageType.Unknown,
                    string.Empty,
                    DateTimeOffset.UtcNow,
                    RawParseStatus.Rejected,
                    ex.Message,
                    false,
                    null,
                    null,
                    null),
                _inboundQueue.Backlog,
                "frame_rejected",
                serverToken);

            _logger.LogWarning(
                ex,
                "frame_rejected sessionId={sessionId} remoteIp={remoteIp} port={port}",
                session.SessionId,
                remoteIp,
                endpoint.Port);
        }
        catch (OperationCanceledException) when (serverToken.IsCancellationRequested)
        {
            closeReason = "server_shutdown";
            _sessionManager.SetCloseReason(session.SessionId, closeReason);
            _logger.LogInformation(
                "session_canceled sessionId={sessionId} remoteIp={remoteIp} port={port}",
                session.SessionId,
                remoteIp,
                endpoint.Port);
        }
        catch (Exception ex)
        {
            closeReason = "session_error";
            _sessionManager.SetCloseReason(session.SessionId, closeReason);
            _logger.LogError(
                ex,
                "session_error sessionId={sessionId} remoteIp={remoteIp} port={port}",
                session.SessionId,
                remoteIp,
                endpoint.Port);
        }
        finally
        {
            if (_sessionManager.TryGet(session.SessionId, out SessionState? openSession) &&
                openSession is not null)
            {
                openSession.DisconnectedAtUtc = DateTimeOffset.UtcNow;
                openSession.CloseReason ??= closeReason;
                await TryUpsertSessionAsync(
                    ToSessionRecord(openSession, isActive: false),
                    "session_close_snapshot",
                    CancellationToken.None);
            }

            _sessionManager.Close(session.SessionId);
            _tcpMetrics.RecordConnectionClosed(endpoint.Port);
            _logger.LogInformation(
                "session_closed sessionId={sessionId} remoteIp={remoteIp} port={port} reason={reason}",
                session.SessionId,
                remoteIp,
                endpoint.Port,
                closeReason);
        }
    }

    private void RegisterInvalidFrame(
        string remoteIp,
        int port,
        ProtocolId protocol,
        SessionId sessionId)
    {
        _abuseGuard.RegisterFrame(remoteIp, isInvalid: true, DateTimeOffset.UtcNow);
        _sessionManager.IncrementFramesInvalid(sessionId);
        _tcpMetrics.RecordFrameInvalid(port, protocol);
        _tcpMetrics.RecordParseResult(port, protocol, success: false);
    }

    private static ReadOnlySpan<byte> FramePreview(ReadOnlyMemory<byte> payload)
    {
        const int maxPreview = 96;
        int size = Math.Min(maxPreview, payload.Length);
        return payload.Span[..size];
    }

    private IFrameDecoder CreateDecoderForEndpoint(TcpServerEndpointOptions endpoint)
    {
        ProtocolId protocol = ProtocolIdParser.Parse(endpoint.Protocol);
        byte[] delimiters = protocol switch
        {
            ProtocolId.Coban => [(byte)';', (byte)'\n'],
            ProtocolId.Cantrack => [(byte)'#', (byte)'\n'],
            _ => [(byte)';', (byte)'#', (byte)'\n']
        };

        return new DelimiterFrameDecoder(delimiters, _options.Socket.MaxFrameBytes);
    }

    private static string ReadText(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return string.Empty;
        }

        return Encoding.ASCII.GetString(payload.Span);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static SessionRecord ToSessionRecord(SessionState state, bool isActive)
    {
        return new SessionRecord(
            state.SessionId,
            state.RemoteIp,
            state.Port,
            state.ConnectedAtUtc,
            state.LastSeenAtUtc,
            state.LastHeartbeatAtUtc,
            state.Imei,
            state.FramesIn,
            state.FramesInvalid,
            state.CloseReason,
            state.DisconnectedAtUtc,
            isActive);
    }

    private async Task<bool> TryAddRawPacketAsync(
        RawPacketRecord record,
        long backlog,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            bool accepted = await _rawPacketQueue.EnqueueAsync(
                new RawPacketEnvelope(record, backlog, DateTimeOffset.UtcNow),
                cancellationToken);
            if (!accepted)
            {
                _tcpMetrics.RecordRawQueueDrop(record.Port, record.Protocol);
                _logger.LogWarning(
                    "raw_queue_drop sessionId={sessionId} packetId={packetId} reason={reason} mode={mode}",
                    record.SessionId,
                    record.PacketId,
                    reason,
                    _rawPacketQueue.FullMode);
                return false;
            }

            _tcpMetrics.RecordRawQueueBacklog(record.Port, record.Protocol, _rawPacketQueue.Backlog);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "raw_queue_enqueue_failed sessionId={sessionId} packetId={packetId} reason={reason}",
                record.SessionId,
                record.PacketId,
                reason);
            return false;
        }
    }

    private async Task TryUpsertSessionAsync(
        SessionRecord session,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ingestionRepository.UpsertSessionAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "persist_session_failed sessionId={sessionId} reason={reason}",
                session.SessionId,
                reason);
        }
    }
}


