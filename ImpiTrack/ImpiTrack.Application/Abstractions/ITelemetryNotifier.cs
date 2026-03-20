namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Contrato para notificacion en tiempo real de eventos de telemetria a clientes conectados.
/// Las implementaciones no deben lanzar excepciones que interrumpan el pipeline de persistencia.
/// </summary>
public interface ITelemetryNotifier
{
    /// <summary>
    /// Notifica una actualizacion de posicion GPS a los usuarios propietarios del dispositivo.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo que reporto la posicion.</param>
    /// <param name="latitude">Latitud en grados decimales.</param>
    /// <param name="longitude">Longitud en grados decimales.</param>
    /// <param name="speedKmh">Velocidad en km/h.</param>
    /// <param name="headingDeg">Rumbo en grados.</param>
    /// <param name="occurredAtUtc">Fecha UTC del evento GPS.</param>
    /// <param name="ignitionOn">Estado del encendido del vehiculo.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task NotifyPositionUpdatedAsync(
        string imei,
        double? latitude,
        double? longitude,
        double? speedKmh,
        int? headingDeg,
        DateTimeOffset occurredAtUtc,
        bool? ignitionOn,
        CancellationToken cancellationToken);

    /// <summary>
    /// Notifica un cambio de estado del dispositivo (Online/Offline/Heartbeat).
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="status">Estado del dispositivo.</param>
    /// <param name="changedAtUtc">Fecha UTC del cambio de estado.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task NotifyDeviceStatusChangedAsync(
        string imei,
        string status,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Notifica un evento de telemetria (ACC_ON, ACC_OFF, PWR_ON, etc.) a los usuarios propietarios.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="eventType">Codigo canonico del evento.</param>
    /// <param name="latitude">Latitud en grados decimales.</param>
    /// <param name="longitude">Longitud en grados decimales.</param>
    /// <param name="occurredAtUtc">Fecha UTC del evento.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task NotifyTelemetryEventAsync(
        string imei,
        string eventType,
        double? latitude,
        double? longitude,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);
}
