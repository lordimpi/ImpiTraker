using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Queue;

namespace ImpiTrack.Tests;

public sealed class QueueBackpressureTests
{
    [Fact]
    public async Task EnqueueAsync_ShouldWaitWhenQueueIsFull()
    {
        var queue = new InMemoryInboundQueue(capacity: 1);
        InboundEnvelope first = CreateEnvelope("A");
        InboundEnvelope second = CreateEnvelope("B");

        await queue.EnqueueAsync(first, CancellationToken.None);
        Task waitingWrite = queue.EnqueueAsync(second, CancellationToken.None).AsTask();

        await Task.Delay(150);
        Assert.False(waitingWrite.IsCompleted);

        InboundEnvelope dequeued = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(first.PacketId, dequeued.PacketId);

        await waitingWrite;
        InboundEnvelope last = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(second.PacketId, last.PacketId);
    }

    private static InboundEnvelope CreateEnvelope(string payload)
    {
        var message = new ParsedMessage(
            ProtocolId.Coban,
            MessageType.Tracking,
            "359586015829802",
            Encoding.ASCII.GetBytes(payload),
            payload,
            DateTimeOffset.UtcNow);

        return new InboundEnvelope(
            SessionId.New(),
            PacketId.New(),
            5001,
            "127.0.0.1",
            message,
            DateTimeOffset.UtcNow);
    }
}
