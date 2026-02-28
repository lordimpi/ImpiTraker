using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Protocols;

/// <summary>
/// Resuelve el protocolo usando primero el mapeo de puertos configurado, con fallback por firma de payload.
/// </summary>
public sealed class PortFirstProtocolResolver : IProtocolResolver
{
    private readonly IReadOnlyDictionary<int, ProtocolId> _byPort;

    /// <summary>
    /// Inicializa un resolvedor con un mapa estatico puerto-protocolo.
    /// </summary>
    /// <param name="byPort">Mapeo configurado por puerto de listener.</param>
    public PortFirstProtocolResolver(IReadOnlyDictionary<int, ProtocolId> byPort)
    {
        _byPort = byPort;
    }

    /// <inheritdoc />
    public ProtocolId Resolve(int port, ReadOnlySpan<byte> preview)
    {
        if (_byPort.TryGetValue(port, out ProtocolId protocol) &&
            protocol != ProtocolId.Unknown)
        {
            return protocol;
        }

        string text = Encoding.ASCII.GetString(preview).Trim();
        if (text.StartsWith("*HQ,", StringComparison.OrdinalIgnoreCase))
        {
            return ProtocolId.Cantrack;
        }

        if (text.StartsWith("##", StringComparison.Ordinal) ||
            text.Contains("imei:", StringComparison.OrdinalIgnoreCase))
        {
            return ProtocolId.Coban;
        }

        return ProtocolId.Unknown;
    }
}
