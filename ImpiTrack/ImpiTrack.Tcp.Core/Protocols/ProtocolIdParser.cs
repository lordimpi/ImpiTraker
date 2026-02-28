using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Protocols;

/// <summary>
/// Convierte nombres de protocolo configurados en valores normalizados de <see cref="ProtocolId"/>.
/// </summary>
public static class ProtocolIdParser
{
    /// <summary>
    /// Parsea un nombre de protocolo desde configuracion.
    /// </summary>
    /// <param name="raw">Valor crudo del protocolo.</param>
    /// <returns><see cref="ProtocolId"/> resuelto o <see cref="ProtocolId.Unknown"/>.</returns>
    public static ProtocolId Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ProtocolId.Unknown;
        }

        return raw.Trim().ToUpperInvariant() switch
        {
            "COBAN" => ProtocolId.Coban,
            "CANTRACK" => ProtocolId.Cantrack,
            _ => ProtocolId.Unknown
        };
    }
}
