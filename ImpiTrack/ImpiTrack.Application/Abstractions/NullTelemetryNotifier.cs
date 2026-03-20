using Microsoft.Extensions.Logging;

namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Implementacion no-op de <see cref="ITelemetryNotifier"/> para uso cuando SignalR no esta disponible.
/// Registrada por defecto en TcpServer standalone; reemplazada por SignalRTelemetryNotifier en el proceso API.
/// </summary>
public sealed class NullTelemetryNotifier : ITelemetryNotifier
{
    private readonly ILogger<NullTelemetryNotifier> _logger;

    /// <summary>
    /// Crea una instancia del notificador no-op.
    /// </summary>
    /// <param name="logger">Logger para registrar notificaciones omitidas.</param>
    public NullTelemetryNotifier(ILogger<NullTelemetryNotifier> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task NotifyPositionUpdatedAsync(
        string imei,
        double? latitude,
        double? longitude,
        double? speedKmh,
        int? headingDeg,
        DateTimeOffset occurredAtUtc,
        bool? ignitionOn,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("signalr_notify_skipped event=PositionUpdated imei={Imei}", imei);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyDeviceStatusChangedAsync(
        string imei,
        string status,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("signalr_notify_skipped event=DeviceStatusChanged imei={Imei} status={Status}", imei, status);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyTelemetryEventAsync(
        string imei,
        string eventType,
        double? latitude,
        double? longitude,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("signalr_notify_skipped event=TelemetryEvent imei={Imei} eventType={EventType}", imei, eventType);
        return Task.CompletedTask;
    }
}
