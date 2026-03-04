using System.Threading.Channels;

namespace TcpServer.RawQueue;

/// <summary>
/// Implementacion en memoria de cola raw con capacidad acotada y politicas configurables.
/// </summary>
public sealed class InMemoryRawPacketQueue : IRawPacketQueue
{
    private readonly Channel<RawPacketEnvelope> _channel;
    private readonly int _capacity;
    private long _backlog;

    /// <summary>
    /// Inicializa la cola raw en memoria.
    /// </summary>
    /// <param name="capacity">Capacidad maxima de elementos retenidos.</param>
    /// <param name="fullMode">Politica de overflow configurada.</param>
    public InMemoryRawPacketQueue(int capacity, RawQueueFullMode fullMode)
    {
        _capacity = Math.Max(1, capacity);
        FullMode = fullMode;
        _channel = Channel.CreateBounded<RawPacketEnvelope>(new BoundedChannelOptions(_capacity)
        {
            FullMode = fullMode == RawQueueFullMode.Wait
                ? BoundedChannelFullMode.Wait
                : BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    /// <inheritdoc />
    public long Backlog => Interlocked.Read(ref _backlog);

    /// <inheritdoc />
    public RawQueueFullMode FullMode { get; }

    /// <inheritdoc />
    public async ValueTask<bool> EnqueueAsync(RawPacketEnvelope envelope, CancellationToken cancellationToken)
    {
        if (FullMode == RawQueueFullMode.Wait)
        {
            await _channel.Writer.WriteAsync(envelope, cancellationToken);
            Interlocked.Increment(ref _backlog);
            return true;
        }

        // Para Drop/Disconnect se rechaza de inmediato cuando no hay capacidad observable.
        if (Backlog >= _capacity)
        {
            return false;
        }

        bool written = _channel.Writer.TryWrite(envelope);
        if (!written)
        {
            return false;
        }

        Interlocked.Increment(ref _backlog);
        return true;
    }

    /// <inheritdoc />
    public ValueTask<RawPacketEnvelope> DequeueAsync(CancellationToken cancellationToken)
    {
        return ReadAndDecrementAsync(cancellationToken);
    }

    private async ValueTask<RawPacketEnvelope> ReadAndDecrementAsync(CancellationToken cancellationToken)
    {
        RawPacketEnvelope value = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _backlog);
        return value;
    }
}
