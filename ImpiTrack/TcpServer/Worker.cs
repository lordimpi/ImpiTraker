using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.Correlation;
using ImpiTrack.Tcp.Core.Framing;
using ImpiTrack.Tcp.Core.Protocols;
using ImpiTrack.Tcp.Core.Queue;
using ImpiTrack.Tcp.Core.Sessions;
using Microsoft.Extensions.Options;

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
    private readonly ISessionManager _sessionManager;
    private readonly IPacketIdGenerator _packetIdGenerator;

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
    public Worker(
        ILogger<Worker> logger,
        IOptions<TcpServerOptions> options,
        IProtocolResolver protocolResolver,
        IEnumerable<IProtocolParser> parsers,
        IEnumerable<IAckStrategy> ackStrategies,
        IInboundQueue inboundQueue,
        ISessionManager sessionManager,
        IPacketIdGenerator packetIdGenerator)
    {
        _logger = logger;
        _options = options.Value;
        _protocolResolver = protocolResolver;
        _parsers = parsers.ToDictionary(x => x.Protocol);
        _ackStrategies = ackStrategies.ToDictionary(x => x.Protocol);
        _inboundQueue = inboundQueue;
        _sessionManager = sessionManager;
        _packetIdGenerator = packetIdGenerator;
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

                while (!serverToken.IsCancellationRequested)
                {
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                    readCts.CancelAfter(TimeSpan.FromSeconds(_options.Socket.ReadTimeoutSeconds));

                    ReadResult result;
                    try
                    {
                        result = await reader.ReadAsync(readCts.Token);
                    }
                    catch (OperationCanceledException) when (!serverToken.IsCancellationRequested)
                    {
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

                        PacketId packetId = _packetIdGenerator.Next();
                        ProtocolId protocol = _protocolResolver.Resolve(endpoint.Port, FramePreview(frame.Payload));

                        if (!_parsers.TryGetValue(protocol, out IProtocolParser? parser))
                        {
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
                            _logger.LogWarning(
                                "parse_fail sessionId={sessionId} packetId={packetId} protocol={protocol} port={port} error={error}",
                                session.SessionId,
                                packetId,
                                protocol,
                                endpoint.Port,
                                parseError ?? "unknown_parse_error");
                            continue;
                        }

                        _sessionManager.AttachImei(session.SessionId, parsed.Imei);

                        if (_ackStrategies.TryGetValue(parsed.Protocol, out IAckStrategy? ackStrategy) &&
                            ackStrategy.TryBuildAck(parsed, out ReadOnlyMemory<byte> ackBytes))
                        {
                            DateTimeOffset ackStarted = DateTimeOffset.UtcNow;
                            await stream.WriteAsync(ackBytes, serverToken);
                            double ackLatencyMs = (DateTimeOffset.UtcNow - ackStarted).TotalMilliseconds;

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

                        await _inboundQueue.EnqueueAsync(
                            new InboundEnvelope(
                                session.SessionId,
                                packetId,
                                endpoint.Port,
                                remoteIp,
                                parsed,
                                DateTimeOffset.UtcNow),
                            serverToken);

                        _logger.LogInformation(
                            "frame_enqueued sessionId={sessionId} packetId={packetId} protocol={protocol} messageType={messageType} imei={imei} port={port} backlog={backlog}",
                            session.SessionId,
                            packetId,
                            parsed.Protocol,
                            parsed.MessageType,
                            parsed.Imei ?? "n/a",
                            endpoint.Port,
                            _inboundQueue.Backlog);
                    }

                    if (!firstFrameReceived &&
                        DateTimeOffset.UtcNow - connectedAt > TimeSpan.FromSeconds(_options.Socket.HandshakeTimeoutSeconds))
                    {
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
                        break;
                    }
                }

                await reader.CompleteAsync();
            }
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(
                ex,
                "frame_rejected sessionId={sessionId} remoteIp={remoteIp} port={port}",
                session.SessionId,
                remoteIp,
                endpoint.Port);
        }
        catch (OperationCanceledException) when (serverToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "session_canceled sessionId={sessionId} remoteIp={remoteIp} port={port}",
                session.SessionId,
                remoteIp,
                endpoint.Port);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "session_error sessionId={sessionId} remoteIp={remoteIp} port={port}",
                session.SessionId,
                remoteIp,
                endpoint.Port);
        }
        finally
        {
            _sessionManager.Close(session.SessionId);
            _logger.LogInformation(
                "session_closed sessionId={sessionId} remoteIp={remoteIp} port={port}",
                session.SessionId,
                remoteIp,
                endpoint.Port);
        }
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
}
