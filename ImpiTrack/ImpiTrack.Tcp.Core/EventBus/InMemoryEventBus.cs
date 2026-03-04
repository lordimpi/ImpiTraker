using System.Collections.Concurrent;

namespace ImpiTrack.Tcp.Core.EventBus;

/// <summary>
/// Implementacion en memoria del bus de eventos para ejecucion local y pruebas.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentQueue<EventBusMessage> _messages = new();

    /// <summary>
    /// Publica un evento en memoria conservando un historial acotado.
    /// </summary>
    /// <typeparam name="TPayload">Tipo de payload a publicar.</typeparam>
    /// <param name="topic">Topic destino.</param>
    /// <param name="payload">Payload del evento.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    public Task PublishAsync<TPayload>(string topic, TPayload payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _messages.Enqueue(new EventBusMessage(topic, payload, DateTimeOffset.UtcNow));

        while (_messages.Count > 5_000 && _messages.TryDequeue(out _))
        {
            // Mantiene memoria acotada para no crecer indefinidamente.
        }

        return Task.CompletedTask;
    }
}
