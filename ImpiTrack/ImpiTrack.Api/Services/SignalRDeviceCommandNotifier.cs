using ImpiTrack.Api.Hubs;
using ImpiTrack.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace ImpiTrack.Api.Services;

/// <summary>
/// Implementacion de <see cref="IDeviceCommandNotifier"/> que envia el evento <c>CommandStatusChanged</c>
/// via SignalR al grupo <c>user-{userId}</c> del propietario del comando.
/// Toda operacion esta envuelta en try/catch: fallos de notificacion NO interrumpen el pipeline de comandos.
/// </summary>
public sealed class SignalRDeviceCommandNotifier : IDeviceCommandNotifier
{
    private readonly IHubContext<DeviceCommandHub> _hub;
    private readonly ILogger<SignalRDeviceCommandNotifier> _logger;

    /// <summary>
    /// Crea un notificador SignalR de comandos de dispositivo.
    /// </summary>
    /// <param name="hub">Contexto del hub para envio de mensajes.</param>
    /// <param name="logger">Logger de diagnostico.</param>
    public SignalRDeviceCommandNotifier(
        IHubContext<DeviceCommandHub> hub,
        ILogger<SignalRDeviceCommandNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyStatusChangedAsync(Guid userId, DeviceCommandRecord command, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group($"user-{userId}").SendAsync(
                "CommandStatusChanged",
                new
                {
                    commandId = command.CommandId,
                    imei = command.Imei,
                    type = command.CommandType,
                    status = command.Status,
                    responseCode = command.ResponseCode,
                    responseText = command.ResponseText,
                    timestampUtc = DateTimeOffset.UtcNow,
                },
                ct);

            _logger.LogDebug(
                "signalr_notify event=CommandStatusChanged commandId={CommandId} userId={UserId} status={Status}",
                command.CommandId,
                userId,
                command.Status);
        }
        catch (Exception ex)
        {
            // Best-effort: log and swallow — do not fail the command flow
            _logger.LogWarning(
                ex,
                "signalr_notify_error event=CommandStatusChanged commandId={CommandId} userId={UserId}",
                command.CommandId,
                userId);
        }
    }
}
