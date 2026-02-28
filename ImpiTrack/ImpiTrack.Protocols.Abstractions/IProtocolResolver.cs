namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Resuelve el protocolo a usar para un frame segun endpoint y vista previa del payload.
/// </summary>
public interface IProtocolResolver
{
    /// <summary>
    /// Resuelve el identificador de protocolo para el frame entrante.
    /// </summary>
    /// <param name="port">Puerto del listener donde se recibio el frame.</param>
    /// <param name="preview">Vista previa corta del payload usada para comparar firmas.</param>
    /// <returns>Valor <see cref="ProtocolId"/> resuelto.</returns>
    ProtocolId Resolve(int port, ReadOnlySpan<byte> preview);
}
