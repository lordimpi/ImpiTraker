namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Evento canonico versionado de telemetria para consumo interno.
/// </summary>
/// <param name="EventVersion">Version semantica del contrato del evento.</param>
/// <param name="EventId">Identificador unico del evento publicado.</param>
/// <param name="OccurredAtUtc">Fecha UTC asociada al evento de dispositivo.</param>
/// <param name="ReceivedAtUtc">Fecha UTC en la que el servidor recibio el frame.</param>
/// <param name="Imei">IMEI del dispositivo fuente.</param>
/// <param name="Protocol">Protocolo de origen normalizado.</param>
/// <param name="MessageType">Tipo canonico de mensaje parseado.</param>
/// <param name="SessionId">Identificador de correlacion de sesion.</param>
/// <param name="PacketId">Identificador de correlacion de paquete.</param>
/// <param name="RemoteIp">IP remota origen del paquete.</param>
/// <param name="Port">Puerto de escucha que recibio el mensaje.</param>
/// <param name="GpsTimeUtc">Timestamp GPS UTC cuando aplica.</param>
/// <param name="Latitude">Latitud en grados decimales cuando aplica.</param>
/// <param name="Longitude">Longitud en grados decimales cuando aplica.</param>
/// <param name="SpeedKmh">Velocidad en km/h cuando aplica.</param>
/// <param name="HeadingDeg">Rumbo en grados cuando aplica.</param>
/// <param name="RawPacketId">Referencia al paquete raw persistido.</param>
/// <param name="IgnitionOn">Estado del ACC del vehiculo. Null si el protocolo no lo provee.</param>
/// <param name="PowerConnected">Indica si el dispositivo tiene alimentacion externa. Null si no disponible.</param>
/// <param name="DoorOpen">Indica si la puerta esta abierta. Null si el protocolo no lo provee.</param>
public sealed record TelemetryEventV1(
    string EventVersion,
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    string Imei,
    ProtocolId Protocol,
    MessageType MessageType,
    SessionId SessionId,
    PacketId PacketId,
    string RemoteIp,
    int Port,
    DateTimeOffset? GpsTimeUtc,
    double? Latitude,
    double? Longitude,
    double? SpeedKmh,
    int? HeadingDeg,
    PacketId RawPacketId,
    bool? IgnitionOn = null,
    bool? PowerConnected = null,
    bool? DoorOpen = null);

/// <summary>
/// Estado de dispositivo publicado por el pipeline interno.
/// </summary>
public enum DeviceStatusKind
{
    /// <summary>
    /// El dispositivo completo su fase de login correctamente.
    /// </summary>
    Online = 1,

    /// <summary>
    /// El dispositivo reporto heartbeat.
    /// </summary>
    Heartbeat = 2,

    /// <summary>
    /// El dispositivo reporto cierre/desconexion de sesion.
    /// </summary>
    Offline = 3
}

/// <summary>
/// Evento canonico versionado para cambios de estado del dispositivo.
/// </summary>
/// <param name="EventVersion">Version semantica del contrato del evento.</param>
/// <param name="EventId">Identificador unico del evento publicado.</param>
/// <param name="OccurredAtUtc">Fecha UTC asociada al estado.</param>
/// <param name="Imei">IMEI del dispositivo origen.</param>
/// <param name="Status">Estado publicado.</param>
/// <param name="Protocol">Protocolo de origen normalizado.</param>
/// <param name="MessageType">Tipo de mensaje que genero el estado.</param>
/// <param name="SessionId">Id de sesion correlacionado.</param>
/// <param name="PacketId">Id de paquete correlacionado.</param>
/// <param name="RemoteIp">IP remota del cliente.</param>
/// <param name="Port">Puerto del listener TCP.</param>
/// <param name="Reason">Motivo breve del estado.</param>
public sealed record DeviceStatusEventV1(
    string EventVersion,
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    string Imei,
    DeviceStatusKind Status,
    ProtocolId Protocol,
    MessageType MessageType,
    SessionId SessionId,
    PacketId PacketId,
    string RemoteIp,
    int Port,
    string Reason);

/// <summary>
/// Evento canonico para dead-letter queue cuando falla la publicacion al bus.
/// </summary>
/// <param name="EventVersion">Version semantica del contrato del evento.</param>
/// <param name="EventId">Identificador unico del evento DLQ.</param>
/// <param name="FailedTopic">Topic original cuya publicacion fallo.</param>
/// <param name="EventType">Tipo logico del evento que fallo.</param>
/// <param name="Imei">IMEI asociado cuando esta disponible.</param>
/// <param name="SessionId">Id de sesion correlacionado.</param>
/// <param name="PacketId">Id de paquete correlacionado.</param>
/// <param name="ExceptionType">Tipo de excepcion capturada.</param>
/// <param name="Error">Mensaje resumido del error.</param>
/// <param name="FailedAtUtc">Fecha UTC en la que fallo la publicacion.</param>
/// <param name="RetryCount">Cantidad de reintentos ejecutados antes de enviar a DLQ.</param>
public sealed record DlqEnvelopeV1(
    string EventVersion,
    Guid EventId,
    string FailedTopic,
    string EventType,
    string? Imei,
    SessionId SessionId,
    PacketId PacketId,
    string ExceptionType,
    string Error,
    DateTimeOffset FailedAtUtc,
    int RetryCount);
