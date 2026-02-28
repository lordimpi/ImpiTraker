using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Configuration;

namespace ImpiTrack.Tcp.Core.Protocols;

/// <summary>
/// Construye tablas de busqueda puerto-protocolo para endpoints desde configuracion.
/// </summary>
public static class ProtocolMapBuilder
{
    /// <summary>
    /// Crea un mapa inmutable de protocolos indexado por puerto TCP.
    /// </summary>
    /// <param name="endpoints">Endpoints TCP configurados.</param>
    /// <returns>Diccionario que asigna cada puerto a un protocolo preferido.</returns>
    public static IReadOnlyDictionary<int, ProtocolId> Build(IEnumerable<TcpServerEndpointOptions> endpoints)
    {
        Dictionary<int, ProtocolId> map = new();
        foreach (TcpServerEndpointOptions endpoint in endpoints)
        {
            map[endpoint.Port] = ProtocolIdParser.Parse(endpoint.Protocol);
        }

        return map;
    }
}
