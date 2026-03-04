namespace ImpiTrack.Tcp.Core.EventBus;

/// <summary>
/// Contrato minimo de bus de eventos interno para publicacion asincrona.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publica un evento en el topic indicado.
    /// </summary>
    /// <typeparam name="TPayload">Tipo del payload del evento.</typeparam>
    /// <param name="topic">Topic destino donde se publicara el evento.</param>
    /// <param name="payload">Carga util del evento.</param>
    /// <param name="cancellationToken">Token de cancelacion de la operacion.</param>
    Task PublishAsync<TPayload>(string topic, TPayload payload, CancellationToken cancellationToken);
}
