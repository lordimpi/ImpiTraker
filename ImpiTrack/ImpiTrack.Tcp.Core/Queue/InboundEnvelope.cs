using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Queue;

/// <summary>
/// Unidad de trabajo encolada despues de las fases de parseo y ACK.
/// </summary>
/// <param name="SessionId">Identificador de correlacion de conexion.</param>
/// <param name="PacketId">Identificador de correlacion de frame.</param>
/// <param name="Port">Puerto del listener donde se recibio el frame.</param>
/// <param name="RemoteIp">Direccion IP remota del cliente.</param>
/// <param name="Message">Mensaje normalizado parseado.</param>
/// <param name="EnqueuedAtUtc">Marca de tiempo UTC de insercion en cola.</param>
public sealed record InboundEnvelope(
    SessionId SessionId,
    PacketId PacketId,
    int Port,
    string RemoteIp,
    ParsedMessage Message,
    DateTimeOffset EnqueuedAtUtc);
