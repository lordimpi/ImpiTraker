using System.Text.Json.Nodes;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Sessions;

namespace TcpServer.BackgroundServices;

/// <summary>
/// Servicio que suscribe al evento <see cref="ISessionManager.ImeiAttached"/> y re-inyecta
/// comandos en estado <c>Queued</c> cada vez que un dispositivo se (re)conecta.
/// El handler es fire-and-forget para no bloquear el ciclo de lectura del Worker.
/// </summary>
public sealed class OutboundCommandReconnectService : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboundCommandReconnectService> _logger;

    /// <summary>
    /// Crea el servicio de re-inyeccion de comandos en reconexion.
    /// </summary>
    /// <param name="sessionManager">Administrador de sesiones TCP activas.</param>
    /// <param name="scopeFactory">Factory para crear scopes DI transitorios.</param>
    /// <param name="logger">Logger estructurado.</param>
    public OutboundCommandReconnectService(
        ISessionManager sessionManager,
        IServiceScopeFactory scopeFactory,
        ILogger<OutboundCommandReconnectService> logger)
    {
        _sessionManager = sessionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.ImeiAttached += OnImeiAttached;
        _logger.LogInformation("outbound_command_reconnect_service_started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.ImeiAttached -= OnImeiAttached;
        _logger.LogInformation("outbound_command_reconnect_service_stopped");
        return Task.CompletedTask;
    }

    private void OnImeiAttached(SessionState session)
    {
        // Fire-and-forget: must not block AttachImei on the read-loop thread.
        _ = Task.Run(async () =>
        {
            try
            {
                await ReplayQueuedCommandsAsync(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "outbound_command_reconnect_replay_error imei={Imei}",
                    session.Imei);
            }
        });
    }

    private async Task ReplayQueuedCommandsAsync(SessionState session)
    {
        string imei = session.Imei!;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IDeviceCommandRepository repo = scope.ServiceProvider.GetRequiredService<IDeviceCommandRepository>();
        IEnumerable<IProtocolCommandSerializer> serializers =
            scope.ServiceProvider.GetRequiredService<IEnumerable<IProtocolCommandSerializer>>();

        IReadOnlyList<DeviceCommandRecord> queued = await repo.GetQueuedByImeiAsync(imei, default);

        if (queued.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "outbound_command_reconnect_replay_started imei={Imei} count={Count}",
            imei,
            queued.Count);

        IProtocolCommandSerializer? serializer =
            serializers.FirstOrDefault(s => s.Protocol == session.Protocol);

        if (serializer is null)
        {
            _logger.LogWarning(
                "outbound_command_reconnect_no_serializer imei={Imei} protocol={Protocol}",
                imei,
                session.Protocol);
            return;
        }

        int enqueued = 0;

        foreach (DeviceCommandRecord record in queued)
        {
            try
            {
                if (!TryParseCommandType(record.CommandType, out DeviceCommandType commandType))
                {
                    _logger.LogWarning(
                        "outbound_command_reconnect_unknown_type commandId={CommandId} commandType={CommandType}",
                        record.CommandId,
                        record.CommandType);
                    continue;
                }

                if (!serializer.Supports(commandType))
                {
                    _logger.LogWarning(
                        "outbound_command_reconnect_unsupported commandId={CommandId} commandType={CommandType} protocol={Protocol}",
                        record.CommandId,
                        record.CommandType,
                        session.Protocol);
                    continue;
                }

                JsonObject? parameters = record.Parameters is not null
                    ? JsonNode.Parse(record.Parameters)?.AsObject()
                    : null;

                var deviceCommand = new DeviceCommand(
                    record.CommandId,
                    imei,
                    commandType,
                    parameters,
                    record.QueuedAtUtc);

                var frame = new OutboundFrame.Command(deviceCommand);

                if (_sessionManager.TryEnqueueCommand(imei, frame))
                {
                    enqueued++;
                    _logger.LogInformation(
                        "outbound_command_reconnect_enqueued commandId={CommandId} imei={Imei}",
                        record.CommandId,
                        imei);
                }
                else
                {
                    _logger.LogWarning(
                        "outbound_command_reconnect_enqueue_failed commandId={CommandId} imei={Imei} (session closed or channel full)",
                        record.CommandId,
                        imei);
                    // Stop trying — session may have disconnected already.
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "outbound_command_reconnect_row_error commandId={CommandId}",
                    record.CommandId);
            }
        }

        _logger.LogInformation(
            "outbound_command_reconnect_replay_completed imei={Imei} enqueued={Enqueued} total={Total}",
            imei,
            enqueued,
            queued.Count);
    }

    private static bool TryParseCommandType(string value, out DeviceCommandType result) =>
        Enum.TryParse(value, ignoreCase: true, out result);
}
