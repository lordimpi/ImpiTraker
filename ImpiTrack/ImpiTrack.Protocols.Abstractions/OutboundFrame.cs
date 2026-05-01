namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Discriminated union de frames salientes encolados en el canal de escritura de sesion.
/// El write-loop es el unico consumidor; multiples productores pueden encolar frames.
/// </summary>
public abstract record OutboundFrame
{
    /// <summary>
    /// Frame de ACK raw (bytes prefabricados) generado por el read-loop para responder
    /// paquetes de login o heartbeat.
    /// </summary>
    /// <param name="Bytes">Bytes a escribir directamente en el NetworkStream.</param>
    public sealed record RawAck(ReadOnlyMemory<byte> Bytes) : OutboundFrame;

    /// <summary>
    /// Frame de comando saliente serializado bajo demanda por el write-loop.
    /// El serializer correspondiente al protocolo de sesion produce los bytes finales.
    /// </summary>
    /// <param name="Cmd">Comando a serializar y enviar al dispositivo.</param>
    public sealed record Command(DeviceCommand Cmd) : OutboundFrame;
}
