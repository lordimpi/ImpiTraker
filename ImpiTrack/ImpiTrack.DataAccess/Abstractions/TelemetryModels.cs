using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.DataAccess.Abstractions;

/// <summary>
/// Resumen de telemetria expuesto para un dispositivo vinculado.
/// </summary>
/// <param name="Imei">IMEI del dispositivo.</param>
/// <param name="BoundAtUtc">Fecha UTC de vinculacion activa.</param>
/// <param name="LastSeenAtUtc">Fecha UTC de ultimo contacto conocido.</param>
/// <param name="ActiveSessionId">Sesion activa actual cuando existe.</param>
/// <param name="Protocol">Ultimo protocolo observado cuando existe.</param>
/// <param name="LastMessageType">Ultimo tipo de mensaje observado cuando existe.</param>
/// <param name="LastPosition">Ultima posicion georreferenciada cuando existe.</param>
public sealed record TelemetryDeviceSummaryDto(
    string Imei,
    DateTimeOffset BoundAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    Guid? ActiveSessionId,
    ProtocolId? Protocol,
    MessageType? LastMessageType,
    LastKnownPositionDto? LastPosition);

/// <summary>
/// Ultima posicion georreferenciada conocida de un dispositivo.
/// </summary>
/// <param name="OccurredAtUtc">Fecha UTC de ocurrencia de la muestra.</param>
/// <param name="ReceivedAtUtc">Fecha UTC de recepcion/persistencia de la muestra.</param>
/// <param name="GpsTimeUtc">Fecha GPS UTC cuando existe.</param>
/// <param name="Latitude">Latitud decimal.</param>
/// <param name="Longitude">Longitud decimal.</param>
/// <param name="SpeedKmh">Velocidad en km/h cuando existe.</param>
/// <param name="HeadingDeg">Rumbo en grados cuando existe.</param>
/// <param name="PacketId">Identificador del paquete correlacionado.</param>
/// <param name="SessionId">Identificador de la sesion correlacionada.</param>
public sealed record LastKnownPositionDto(
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? GpsTimeUtc,
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    int? HeadingDeg,
    Guid PacketId,
    Guid SessionId);

/// <summary>
/// Punto de historial georreferenciado para un dispositivo.
/// </summary>
/// <param name="OccurredAtUtc">Fecha UTC de ocurrencia de la muestra.</param>
/// <param name="ReceivedAtUtc">Fecha UTC de recepcion/persistencia de la muestra.</param>
/// <param name="GpsTimeUtc">Fecha GPS UTC cuando existe.</param>
/// <param name="Latitude">Latitud decimal.</param>
/// <param name="Longitude">Longitud decimal.</param>
/// <param name="SpeedKmh">Velocidad en km/h cuando existe.</param>
/// <param name="HeadingDeg">Rumbo en grados cuando existe.</param>
/// <param name="PacketId">Identificador del paquete correlacionado.</param>
/// <param name="SessionId">Identificador de la sesion correlacionada.</param>
public sealed record DevicePositionPointDto(
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? GpsTimeUtc,
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    int? HeadingDeg,
    Guid PacketId,
    Guid SessionId);

/// <summary>
/// Evento reciente asociado a un dispositivo.
/// </summary>
/// <param name="EventId">Identificador del evento.</param>
/// <param name="OccurredAtUtc">Fecha UTC de ocurrencia del evento.</param>
/// <param name="ReceivedAtUtc">Fecha UTC de recepcion/persistencia del evento.</param>
/// <param name="EventCode">Codigo canonico del evento.</param>
/// <param name="PayloadText">Payload textual de respaldo.</param>
/// <param name="Protocol">Protocolo asociado al evento.</param>
/// <param name="MessageType">Tipo de mensaje asociado.</param>
/// <param name="PacketId">Identificador del paquete correlacionado.</param>
/// <param name="SessionId">Identificador de la sesion correlacionada.</param>
public sealed record DeviceEventDto(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    string EventCode,
    string PayloadText,
    ProtocolId Protocol,
    MessageType MessageType,
    Guid PacketId,
    Guid SessionId);

/// <summary>
/// Resumen legible de un recorrido vehicular construido desde telemetria historica.
/// </summary>
/// <param name="TripId">Identificador deterministico del recorrido.</param>
/// <param name="Imei">IMEI del dispositivo.</param>
/// <param name="StartedAtUtc">Fecha UTC de inicio del recorrido.</param>
/// <param name="EndedAtUtc">Fecha UTC de fin del recorrido cuando existe.</param>
/// <param name="PointCount">Cantidad de puntos usados para construir el recorrido.</param>
/// <param name="MaxSpeedKmh">Velocidad maxima observada en km/h cuando existe.</param>
/// <param name="AvgSpeedKmh">Velocidad promedio observada en km/h cuando existe.</param>
/// <param name="StartPosition">Primer punto del recorrido.</param>
/// <param name="EndPosition">Ultimo punto del recorrido.</param>
public sealed record TripSummaryDto(
    string TripId,
    string Imei,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int PointCount,
    double? MaxSpeedKmh,
    double? AvgSpeedKmh,
    DevicePositionPointDto StartPosition,
    DevicePositionPointDto EndPosition);

/// <summary>
/// Detalle completo de un recorrido vehicular construido desde telemetria historica.
/// </summary>
/// <param name="TripId">Identificador deterministico del recorrido.</param>
/// <param name="Imei">IMEI del dispositivo.</param>
/// <param name="StartedAtUtc">Fecha UTC de inicio del recorrido.</param>
/// <param name="EndedAtUtc">Fecha UTC de fin del recorrido cuando existe.</param>
/// <param name="PointCount">Cantidad de puntos usados para construir el recorrido.</param>
/// <param name="MaxSpeedKmh">Velocidad maxima observada en km/h cuando existe.</param>
/// <param name="AvgSpeedKmh">Velocidad promedio observada en km/h cuando existe.</param>
/// <param name="PathPoints">Puntos completos del recorrido.</param>
/// <param name="StartPosition">Primer punto del recorrido.</param>
/// <param name="EndPosition">Ultimo punto del recorrido.</param>
/// <param name="SourceRule">Regla usada para construir el recorrido.</param>
public sealed record TripDetailDto(
    string TripId,
    string Imei,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int PointCount,
    double? MaxSpeedKmh,
    double? AvgSpeedKmh,
    IReadOnlyList<DevicePositionPointDto> PathPoints,
    DevicePositionPointDto StartPosition,
    DevicePositionPointDto EndPosition,
    string SourceRule);
