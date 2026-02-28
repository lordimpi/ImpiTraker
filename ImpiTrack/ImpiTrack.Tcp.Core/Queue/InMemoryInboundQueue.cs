using System.Threading.Channels;

namespace ImpiTrack.Tcp.Core.Queue;

/// <summary>
/// Implementacion en memoria con canal acotado para envelopes entrantes.
/// </summary>
public sealed class InMemoryInboundQueue : IInboundQueue
{
    private readonly Channel<InboundEnvelope> _channel;
    private long _backlog;

    /// <summary>
    /// Inicializa la cola con capacidad acotada y backpressure basado en espera.
    /// </summary>
    /// <param name="capacity">Numero maximo de envelopes retenidos en memoria.</param>
    public InMemoryInboundQueue(int capacity)
    {
        _channel = Channel.CreateBounded<InboundEnvelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false,
            AllowSynchronousContinuations = false
        });
    }

    /// <inheritdoc />
    public long Backlog => Interlocked.Read(ref _backlog);

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(InboundEnvelope envelope, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(envelope, cancellationToken);
        Interlocked.Increment(ref _backlog);
    }

    /// <inheritdoc />
    public ValueTask<InboundEnvelope> DequeueAsync(CancellationToken cancellationToken)
    {
        return ReadAndDecrementAsync(cancellationToken);
    }

    private async ValueTask<InboundEnvelope> ReadAndDecrementAsync(CancellationToken cancellationToken)
    {
        InboundEnvelope value = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _backlog);
        return value;
    }
}
