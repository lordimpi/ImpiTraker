namespace ImpiTrack.Tcp.Core.Queue;

/// <summary>
/// Abstraccion de cola acotada para mensajes entrantes parseados.
/// </summary>
public interface IInboundQueue
{
    /// <summary>
    /// Obtiene el numero actual de envelopes en cola pendientes de consumo.
    /// </summary>
    long Backlog { get; }

    /// <summary>
    /// Encola un envelope parseado aplicando la semantica de backpressure configurada.
    /// </summary>
    /// <param name="envelope">Envelope de mensaje a encolar.</param>
    /// <param name="cancellationToken">Token de cancelacion para la operacion de encolado.</param>
    ValueTask EnqueueAsync(InboundEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>
    /// Desencola el siguiente envelope disponible.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion para la operacion de desencolado.</param>
    /// <returns>Siguiente envelope en cola.</returns>
    ValueTask<InboundEnvelope> DequeueAsync(CancellationToken cancellationToken);
}
