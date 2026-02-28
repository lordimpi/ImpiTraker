using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Correlation;

/// <summary>
/// Generador de id de paquete respaldado por valores GUID aleatorios.
/// </summary>
public sealed class GuidPacketIdGenerator : IPacketIdGenerator
{
    /// <inheritdoc />
    public PacketId Next() => PacketId.New();
}
