using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Correlation;

/// <summary>
/// Genera identificadores de correlacion de paquete.
/// </summary>
public interface IPacketIdGenerator
{
    /// <summary>
    /// Devuelve el siguiente valor de identificador de paquete.
    /// </summary>
    /// <returns>Nuevo identificador de paquete.</returns>
    PacketId Next();
}
