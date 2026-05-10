namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Serializa comandos salientes hacia el formato de wire del protocolo especifico.
/// Cada implementacion maneja exactamente un <see cref="ProtocolId"/>.
/// </summary>
public interface IProtocolCommandSerializer
{
    /// <summary>
    /// Protocolo de red al que corresponde este serializer.
    /// </summary>
    ProtocolId Protocol { get; }

    /// <summary>
    /// Indica si el protocolo soporta el tipo de comando especificado.
    /// </summary>
    /// <param name="type">Tipo de comando a verificar.</param>
    /// <returns><c>true</c> si el comando puede serializarse para este protocolo.</returns>
    bool Supports(DeviceCommandType type);

    /// <summary>
    /// Indica si el dispositivo enviara un ACK numerico para el tipo de comando especificado.
    /// Cuando retorna <c>false</c>, el write-loop debe transicionar el comando directamente
    /// a <c>Acknowledged</c> tras el envio exitoso, sin esperar respuesta del dispositivo.
    /// </summary>
    /// <param name="type">Tipo de comando a verificar.</param>
    /// <returns><c>true</c> si se espera ACK del dispositivo; <c>false</c> para comandos sin ACK.</returns>
    bool AckExpected(DeviceCommandType type);

    /// <summary>
    /// Serializa el comando a los bytes de wire correspondientes al protocolo.
    /// </summary>
    /// <param name="command">Comando a serializar.</param>
    /// <returns>Bytes listos para escribir en el <c>NetworkStream</c>.</returns>
    /// <exception cref="CommandNotSupportedByProtocolException">
    /// El tipo de comando no esta soportado por este protocolo.
    /// </exception>
    /// <exception cref="CommandParameterMissingException">
    /// Falta un parametro requerido en <see cref="DeviceCommand.Parameters"/>.
    /// </exception>
    ReadOnlyMemory<byte> Serialize(DeviceCommand command);
}
