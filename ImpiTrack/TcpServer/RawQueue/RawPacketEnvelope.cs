using ImpiTrack.Ops;

namespace TcpServer.RawQueue;

/// <summary>
/// Unidad encolada para persistencia diferida de paquete raw.
/// </summary>
/// <param name="Record">Registro raw a persistir.</param>
/// <param name="InboundBacklog">Backlog observado de cola de envelopes parseados.</param>
/// <param name="EnqueuedAtUtc">Fecha UTC en que se encolo para persistencia.</param>
public sealed record RawPacketEnvelope(
    RawPacketRecord Record,
    long InboundBacklog,
    DateTimeOffset EnqueuedAtUtc);
