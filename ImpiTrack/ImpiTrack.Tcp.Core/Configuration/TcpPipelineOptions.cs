namespace ImpiTrack.Tcp.Core.Configuration;

/// <summary>
/// Configuracion de capacidad de cola y consumidores para procesamiento de mensajes entrantes.
/// </summary>
public sealed class TcpPipelineOptions
{
    /// <summary>
    /// Capacidad del canal acotado usada para backpressure.
    /// </summary>
    public int ChannelCapacity { get; set; } = 20_000;

    /// <summary>
    /// Numero de workers en segundo plano que consumen envelopes en cola.
    /// </summary>
    public int ConsumerWorkers { get; set; } = 2;
}
