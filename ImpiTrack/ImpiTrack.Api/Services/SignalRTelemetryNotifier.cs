using ImpiTrack.Api.Hubs;
using ImpiTrack.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace ImpiTrack.Api.Services;

/// <summary>
/// Implementacion de <see cref="ITelemetryNotifier"/> que envia eventos via SignalR
/// a los grupos <c>user_{userId}</c> de los propietarios del dispositivo.
/// Envuelve toda operacion en try/catch: fallos de notificacion NO interrumpen el pipeline de persistencia.
/// </summary>
public sealed class SignalRTelemetryNotifier : ITelemetryNotifier
{
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly IDeviceOwnershipResolver _ownershipResolver;
    private readonly ILogger<SignalRTelemetryNotifier> _logger;

    /// <summary>
    /// Crea un notificador SignalR de telemetria en tiempo real.
    /// </summary>
    /// <param name="hubContext">Contexto del hub para envio de mensajes.</param>
    /// <param name="ownershipResolver">Resolver de IMEI a userId(s).</param>
    /// <param name="logger">Logger de diagnostico.</param>
    public SignalRTelemetryNotifier(
        IHubContext<TelemetryHub> hubContext,
        IDeviceOwnershipResolver ownershipResolver,
        ILogger<SignalRTelemetryNotifier> logger)
    {
        _hubContext = hubContext;
        _ownershipResolver = ownershipResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyPositionUpdatedAsync(
        string imei,
        double? latitude,
        double? longitude,
        double? speedKmh,
        int? headingDeg,
        DateTimeOffset occurredAtUtc,
        bool? ignitionOn,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Guid> userIds = await _ownershipResolver.GetUserIdsForImeiAsync(imei, cancellationToken);
            if (userIds.Count == 0)
            {
                return;
            }

            var message = new PositionUpdatedMessage(
                imei, latitude, longitude, speedKmh, headingDeg, occurredAtUtc, ignitionOn);

            foreach (Guid userId in userIds)
            {
                await _hubContext.Clients
                    .Group($"user_{userId}")
                    .SendAsync("PositionUpdated", message, cancellationToken);
            }

            _logger.LogDebug(
                "signalr_notify event=PositionUpdated imei={Imei} recipients={Count}",
                imei,
                userIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "signalr_notify_error event=PositionUpdated imei={Imei}",
                imei);
        }
    }

    /// <inheritdoc />
    public async Task NotifyDeviceStatusChangedAsync(
        string imei,
        string status,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Guid> userIds = await _ownershipResolver.GetUserIdsForImeiAsync(imei, cancellationToken);
            if (userIds.Count == 0)
            {
                return;
            }

            var message = new DeviceStatusChangedMessage(imei, status, changedAtUtc);

            foreach (Guid userId in userIds)
            {
                await _hubContext.Clients
                    .Group($"user_{userId}")
                    .SendAsync("DeviceStatusChanged", message, cancellationToken);
            }

            _logger.LogDebug(
                "signalr_notify event=DeviceStatusChanged imei={Imei} status={Status} recipients={Count}",
                imei,
                status,
                userIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "signalr_notify_error event=DeviceStatusChanged imei={Imei}",
                imei);
        }
    }

    /// <inheritdoc />
    public async Task NotifyTelemetryEventAsync(
        string imei,
        string eventType,
        double? latitude,
        double? longitude,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Guid> userIds = await _ownershipResolver.GetUserIdsForImeiAsync(imei, cancellationToken);
            if (userIds.Count == 0)
            {
                return;
            }

            var message = new TelemetryEventOccurredMessage(imei, eventType, latitude, longitude, occurredAtUtc);

            foreach (Guid userId in userIds)
            {
                await _hubContext.Clients
                    .Group($"user_{userId}")
                    .SendAsync("TelemetryEventOccurred", message, cancellationToken);
            }

            _logger.LogDebug(
                "signalr_notify event=TelemetryEventOccurred imei={Imei} eventType={EventType} recipients={Count}",
                imei,
                eventType,
                userIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "signalr_notify_error event=TelemetryEventOccurred imei={Imei}",
                imei);
        }
    }
}
