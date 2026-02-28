namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Construye confirmaciones salientes para mensajes de protocolo parseados.
/// </summary>
public interface IAckStrategy
{
    /// <summary>
    /// Obtiene el protocolo manejado por esta estrategia de ACK.
    /// </summary>
    ProtocolId Protocol { get; }

    /// <summary>
    /// Intenta construir bytes de ACK para el mensaje entregado.
    /// </summary>
    /// <param name="message">Mensaje parseado a confirmar.</param>
    /// <param name="ackBytes">Bytes de payload ACK cuando estan disponibles.</param>
    /// <returns><c>true</c> cuando se debe enviar ACK; de lo contrario <c>false</c>.</returns>
    bool TryBuildAck(in ParsedMessage message, out ReadOnlyMemory<byte> ackBytes);
}
