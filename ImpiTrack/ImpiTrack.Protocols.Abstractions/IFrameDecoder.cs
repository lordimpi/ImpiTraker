using System.Buffers;

namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Extrae frames completos de protocolo desde un buffer de bytes continuo.
/// </summary>
public interface IFrameDecoder
{
    /// <summary>
    /// Intenta consumir un frame completo desde el buffer actual.
    /// </summary>
    /// <param name="buffer">Ventana de buffer para inspeccionar y consumir.</param>
    /// <param name="frame">Frame decodificado cuando esta disponible.</param>
    /// <returns><c>true</c> cuando se decodifica un frame; de lo contrario <c>false</c>.</returns>
    bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out Frame frame);
}
