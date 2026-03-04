using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using TcpServer.RawQueue;

namespace ImpiTrack.Tests;

public sealed class RawPacketQueueTests
{
    [Fact]
    public async Task EnqueueAsync_ShouldWaitWhenFullModeIsWait()
    {
        var queue = new InMemoryRawPacketQueue(capacity: 1, RawQueueFullMode.Wait);
        RawPacketEnvelope first = CreateEnvelope("A");
        RawPacketEnvelope second = CreateEnvelope("B");

        bool firstAccepted = await queue.EnqueueAsync(first, CancellationToken.None);
        Task<bool> pending = queue.EnqueueAsync(second, CancellationToken.None).AsTask();

        await Task.Delay(120);
        Assert.True(firstAccepted);
        Assert.False(pending.IsCompleted);

        RawPacketEnvelope dequeued = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(first.Record.PacketId, dequeued.Record.PacketId);

        bool secondAccepted = await pending;
        Assert.True(secondAccepted);
    }

    [Fact]
    public async Task EnqueueAsync_ShouldDropWhenFullModeIsDrop()
    {
        var queue = new InMemoryRawPacketQueue(capacity: 1, RawQueueFullMode.Drop);
        RawPacketEnvelope first = CreateEnvelope("A");
        RawPacketEnvelope second = CreateEnvelope("B");

        bool firstAccepted = await queue.EnqueueAsync(first, CancellationToken.None);
        bool secondAccepted = await queue.EnqueueAsync(second, CancellationToken.None);

        Assert.True(firstAccepted);
        Assert.False(secondAccepted);
    }

    [Fact]
    public async Task EnqueueAsync_ShouldRejectWhenFullModeIsDisconnect()
    {
        var queue = new InMemoryRawPacketQueue(capacity: 1, RawQueueFullMode.Disconnect);
        RawPacketEnvelope first = CreateEnvelope("A");
        RawPacketEnvelope second = CreateEnvelope("B");

        bool firstAccepted = await queue.EnqueueAsync(first, CancellationToken.None);
        bool secondAccepted = await queue.EnqueueAsync(second, CancellationToken.None);

        Assert.True(firstAccepted);
        Assert.False(secondAccepted);
    }

    private static RawPacketEnvelope CreateEnvelope(string payload)
    {
        var record = new RawPacketRecord(
            SessionId.New(),
            PacketId.New(),
            5001,
            "127.0.0.1",
            ProtocolId.Coban,
            "359586015829802",
            MessageType.Tracking,
            payload,
            DateTimeOffset.UtcNow,
            RawParseStatus.Ok,
            null,
            true,
            "ON\r\n",
            DateTimeOffset.UtcNow,
            1.0);

        return new RawPacketEnvelope(record, 0, DateTimeOffset.UtcNow);
    }
}
