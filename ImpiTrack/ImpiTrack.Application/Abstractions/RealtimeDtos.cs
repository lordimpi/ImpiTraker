namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// DTO para notificacion en tiempo real de actualizacion de posicion GPS.
/// </summary>
/// <param name="Imei">IMEI del dispositivo que reporto la posicion.</param>
/// <param name="Latitude">Latitud en grados decimales.</param>
/// <param name="Longitude">Longitud en grados decimales.</param>
/// <param name="SpeedKmh">Velocidad en km/h.</param>
/// <param name="HeadingDeg">Rumbo en grados.</param>
/// <param name="OccurredAtUtc">Fecha UTC del evento GPS.</param>
/// <param name="IgnitionOn">Estado del encendido del vehiculo.</param>
public sealed record PositionUpdatedMessage(
    string Imei,
    double? Latitude,
    double? Longitude,
    double? SpeedKmh,
    int? HeadingDeg,
    DateTimeOffset OccurredAtUtc,
    bool? IgnitionOn);

/// <summary>
/// DTO para notificacion en tiempo real de cambio de estado del dispositivo.
/// </summary>
/// <param name="Imei">IMEI del dispositivo.</param>
/// <param name="Status">Estado actual del dispositivo (Online, Offline, Heartbeat).</param>
/// <param name="ChangedAtUtc">Fecha UTC del cambio de estado.</param>
public sealed record DeviceStatusChangedMessage(
    string Imei,
    string Status,
    DateTimeOffset ChangedAtUtc);

/// <summary>
/// DTO para notificacion en tiempo real de evento de telemetria (ACC, PWR, etc.).
/// </summary>
/// <param name="Imei">IMEI del dispositivo.</param>
/// <param name="EventType">Codigo canonico del evento (ACC_ON, ACC_OFF, PWR_ON, etc.).</param>
/// <param name="Latitude">Latitud en grados decimales.</param>
/// <param name="Longitude">Longitud en grados decimales.</param>
/// <param name="OccurredAtUtc">Fecha UTC del evento.</param>
public sealed record TelemetryEventOccurredMessage(
    string Imei,
    string EventType,
    double? Latitude,
    double? Longitude,
    DateTimeOffset OccurredAtUtc);
