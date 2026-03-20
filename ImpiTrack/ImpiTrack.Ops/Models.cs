using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Ops;

/// <summary>
/// Estado de parseo persistido para un paquete raw.
/// </summary>
public enum RawParseStatus
{
    /// <summary>
    /// El paquete fue parseado correctamente.
    /// </summary>
    Ok = 1,

    /// <summary>
    /// El paquete no pudo parsearse.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// El paquete fue rechazado por reglas de framing o limites.
    /// </summary>
    Rejected = 3
}

/// <summary>
/// Registro de evidencia raw para diagnostico operativo.
/// </summary>
/// <param name="SessionId">Id de correlacion de sesion TCP.</param>
/// <param name="PacketId">Id de correlacion de paquete.</param>
/// <param name="Port">Puerto de listener donde se recibio el paquete.</param>
/// <param name="RemoteIp">IP remota del cliente.</param>
/// <param name="Protocol">Protocolo resuelto del paquete.</param>
/// <param name="Imei">IMEI extraido cuando esta disponible.</param>
/// <param name="MessageType">Tipo canonico de mensaje.</param>
/// <param name="PayloadText">Payload en texto para inspeccion.</param>
/// <param name="ReceivedAtUtc">Fecha UTC de recepcion.</param>
/// <param name="ParseStatus">Resultado de parseo aplicado al paquete.</param>
/// <param name="ParseError">Codigo corto de error de parseo cuando aplica.</param>
/// <param name="AckSent">Indica si se envio ACK.</param>
/// <param name="AckPayload">Payload ACK enviado, truncado para diagnostico.</param>
/// <param name="AckAtUtc">Fecha UTC de envio de ACK.</param>
/// <param name="AckLatencyMs">Latencia de ACK en milisegundos.</param>
public sealed record RawPacketRecord(
    SessionId SessionId,
    PacketId PacketId,
    int Port,
    string RemoteIp,
    ProtocolId Protocol,
    string? Imei,
    MessageType MessageType,
    string PayloadText,
    DateTimeOffset ReceivedAtUtc,
    RawParseStatus ParseStatus,
    string? ParseError,
    bool AckSent,
    string? AckPayload,
    DateTimeOffset? AckAtUtc,
    double? AckLatencyMs);

/// <summary>
/// Snapshot de sesion TCP para diagnostico operativo.
/// </summary>
/// <param name="SessionId">Id de correlacion de sesion.</param>
/// <param name="RemoteIp">IP remota del cliente.</param>
/// <param name="Port">Puerto del listener.</param>
/// <param name="ConnectedAtUtc">Fecha UTC de apertura de sesion.</param>
/// <param name="LastSeenAtUtc">Fecha UTC del ultimo frame visto.</param>
/// <param name="LastHeartbeatAtUtc">Fecha UTC del ultimo heartbeat.</param>
/// <param name="Imei">IMEI asociado cuando se conoce.</param>
/// <param name="FramesIn">Cantidad de frames recibidos en la sesion.</param>
/// <param name="FramesInvalid">Cantidad de frames invalidos en la sesion.</param>
/// <param name="CloseReason">Motivo de cierre de la sesion.</param>
/// <param name="DisconnectedAtUtc">Fecha UTC de cierre de sesion.</param>
/// <param name="IsActive">Indica si la sesion sigue activa.</param>
public sealed record SessionRecord(
    SessionId SessionId,
    string RemoteIp,
    int Port,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    string? Imei,
    long FramesIn,
    long FramesInvalid,
    string? CloseReason,
    DateTimeOffset? DisconnectedAtUtc,
    bool IsActive);

/// <summary>
/// Resumen de errores de parseo agrupados para endpoints de diagnostico.
/// </summary>
/// <param name="GroupKey">Llave de agrupacion.</param>
/// <param name="Count">Cantidad de paquetes en el grupo.</param>
/// <param name="LastPacketId">Ultimo paquete observado para el grupo.</param>
public sealed record ErrorAggregate(
    string GroupKey,
    long Count,
    PacketId? LastPacketId);

/// <summary>
/// Parametros de consulta paginada para paquetes raw recientes.
/// </summary>
/// <param name="Page">Pagina solicitada (base 1).</param>
/// <param name="PageSize">Tamano de pagina (valores permitidos: 10, 20, 50, 100).</param>
/// <param name="Imei">IMEI opcional para filtrar.</param>
public sealed record OpsRawListQuery(int Page, int PageSize, string? Imei);

/// <summary>
/// Parametros de consulta paginada para sesiones activas.
/// </summary>
/// <param name="Page">Pagina solicitada (base 1).</param>
/// <param name="PageSize">Tamano de pagina (valores permitidos: 10, 20, 50, 100).</param>
/// <param name="Port">Puerto opcional para filtrar.</param>
public sealed record OpsSessionListQuery(int Page, int PageSize, int? Port);

/// <summary>
/// Parametros de consulta paginada para agregados de errores de parseo.
/// </summary>
/// <param name="Page">Pagina solicitada (base 1).</param>
/// <param name="PageSize">Tamano de pagina (valores permitidos: 10, 20, 50, 100).</param>
/// <param name="From">Inicio opcional del rango UTC.</param>
/// <param name="To">Fin opcional del rango UTC.</param>
/// <param name="GroupBy">Criterio de agrupacion: protocol, port o errorCode.</param>
public sealed record OpsErrorListQuery(int Page, int PageSize, DateTimeOffset? From, DateTimeOffset? To, string GroupBy);

/// <summary>
/// Snapshot de ingesta por puerto para monitoreo operativo.
/// </summary>
/// <param name="Port">Puerto del listener.</param>
/// <param name="ActiveConnections">Cantidad de conexiones activas.</param>
/// <param name="FramesIn">Cantidad acumulada de frames recibidos.</param>
/// <param name="ParseOk">Cantidad acumulada de parseos exitosos.</param>
/// <param name="ParseFail">Cantidad acumulada de parseos fallidos/rechazados.</param>
/// <param name="AckSent">Cantidad acumulada de ACK enviados.</param>
/// <param name="Backlog">Backlog observado de la cola de procesamiento.</param>
public sealed record PortIngestionSnapshot(
    int Port,
    int ActiveConnections,
    long FramesIn,
    long ParseOk,
    long ParseFail,
    long AckSent,
    long Backlog);
