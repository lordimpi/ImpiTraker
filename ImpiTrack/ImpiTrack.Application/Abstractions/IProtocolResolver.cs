using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Resuelve el protocolo persistido de un dispositivo (por IMEI) consultando primero la
/// tabla <c>user_devices</c> y, como fallback, la posicion mas reciente en <c>positions</c>.
/// Distinto de <see cref="ImpiTrack.Protocols.Abstractions.IProtocolResolver"/> el cual resuelve
/// el protocolo de un frame entrante a partir del puerto y la firma del payload.
/// </summary>
public interface IDeviceProtocolResolver
{
    /// <summary>
    /// Resuelve el protocolo del dispositivo indicado.
    /// Devuelve <c>null</c> cuando no es posible determinar el protocolo (deberia mapearse
    /// a un error <c>protocol_unknown</c> en el caller).
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Protocolo resuelto o null si no fue determinado.</returns>
    Task<ProtocolId?> ResolveForDeviceAsync(string imei, CancellationToken cancellationToken);
}
