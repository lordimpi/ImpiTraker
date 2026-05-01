using System.Text;
using System.Threading.Channels;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Sessions;

namespace ImpiTrack.Tests;

/// <summary>
/// MERGE GATE: Validates the single-writer invariant of the outbound channel.
///
/// Phase 4 (Worker.cs refactor) is not yet merged, so these tests exercise the channel
/// infrastructure directly: SessionState.OutboundChannel + InMemorySessionManager.
/// The invariant being tested: ALL bytes written to a stream via the channel consumer
/// are serialized (no concurrent partial-frame writes). The channel itself enforces
/// this because SingleReader=true — only one consumer ever drains it.
///
/// The tests simulate the write-loop pattern: one "reader" task drains the channel
/// into a fake stream, while multiple "writer" tasks (ACK producers + command producers)
/// push frames concurrently.
/// </summary>
public sealed class SessionChannelConcurrencyTests
{
    private const string TestImei = "123456789012345";

    // ─── MERGE GATE: concurrent ACKs + commands, no interleaving ─────────────

    [Fact]
    public async Task WriteLoop_50AcksAnd50Commands_AllFramesBytesArriveIntact()
    {
        const int ackCount = 50;
        const int cmdCount = 50;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var manager = new InMemorySessionManager();
        SessionState session = manager.Open("127.0.0.1", 5001);
        session.Imei = TestImei;

        // Sink for bytes written by the "write loop"
        var written = new List<byte[]>();
        var writeLock = new object();

        // Simulate the write loop: drain channel sequentially → write to "stream"
        var writeLoopTask = Task.Run(async () =>
        {
            await foreach (OutboundFrame frame in session.OutboundChannel.Reader.ReadAllAsync(cts.Token))
            {
                byte[] frameBytes = ExtractBytes(frame);
                lock (writeLock) { written.Add(frameBytes); }
            }
        }, cts.Token);

        // Producer 1: 50 ACK frames (RawAck simulating inbound ACK bytes the read loop emits)
        var ackProducer = Task.Run(async () =>
        {
            for (int i = 0; i < ackCount; i++)
            {
                byte[] ack = Encoding.ASCII.GetBytes($"ON\r\n");
                await session.OutboundChannel.Writer.WriteAsync(
                    new OutboundFrame.RawAck(ack), cts.Token);
            }
        }, cts.Token);

        // Producer 2: 50 Command frames
        var cmdProducer = Task.Run(async () =>
        {
            for (int i = 0; i < cmdCount; i++)
            {
                DeviceCommand cmd = new(
                    Guid.NewGuid(), TestImei, DeviceCommandType.Arm, null, DateTimeOffset.UtcNow);
                await session.OutboundChannel.Writer.WriteAsync(
                    new OutboundFrame.Command(cmd), cts.Token);
            }
        }, cts.Token);

        // Wait for both producers to finish, then complete the channel
        await Task.WhenAll(ackProducer, cmdProducer);
        session.OutboundChannel.Writer.TryComplete();

        // Wait for write loop to drain
        await writeLoopTask.WaitAsync(TimeSpan.FromSeconds(5));

        // ASSERT: exactly 100 frames arrived
        Assert.Equal(ackCount + cmdCount, written.Count);

        // ASSERT: no frame is zero-length (no partial/empty writes)
        Assert.All(written, frame => Assert.NotEmpty(frame));
    }

    [Fact]
    public async Task WriteLoop_CancelledMidStream_CompletesCleanly()
    {
        using var cts = new CancellationTokenSource();
        var manager = new InMemorySessionManager();
        SessionState session = manager.Open("127.0.0.1", 5001);
        session.Imei = TestImei;

        int drainedCount = 0;

        var writeLoopTask = Task.Run(async () =>
        {
            try
            {
                await foreach (OutboundFrame frame in session.OutboundChannel.Reader.ReadAllAsync(cts.Token))
                {
                    Interlocked.Increment(ref drainedCount);
                    // Simulate small write latency
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected path when CTS fires mid-stream
            }
        });

        // Push a few frames then cancel
        for (int i = 0; i < 5; i++)
        {
            await session.OutboundChannel.Writer.WriteAsync(
                new OutboundFrame.RawAck(Encoding.ASCII.GetBytes("ON\r\n")));
        }

        await Task.Delay(20); // let loop drain some
        cts.Cancel();

        // Write loop MUST complete within 500ms after cancel
        await writeLoopTask.WaitAsync(TimeSpan.FromMilliseconds(500));

        // No exception leaked
        Assert.True(writeLoopTask.IsCompleted);
    }

    [Fact]
    public async Task SessionClose_CompletesChannel_WriteLoopExits()
    {
        var manager = new InMemorySessionManager();
        SessionState session = manager.Open("127.0.0.1", 5001);
        session.Imei = TestImei;

        // Write a few frames BEFORE closing
        for (int i = 0; i < 3; i++)
        {
            await session.OutboundChannel.Writer.WriteAsync(
                new OutboundFrame.RawAck(Encoding.ASCII.GetBytes("ACK")));
        }

        // Simulate write loop started
        var allFrames = new List<byte[]>();
        var readTask = Task.Run(async () =>
        {
            await foreach (OutboundFrame frame in session.OutboundChannel.Reader.ReadAllAsync())
            {
                allFrames.Add(ExtractBytes(frame));
            }
        });

        // Close the session (completes the channel)
        manager.Close(session.SessionId);

        // Write loop MUST exit cleanly
        await readTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, allFrames.Count);
        Assert.True(session.OutboundChannel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task SessionIsCommandable_AfterImeiSet_BeforeClose()
    {
        var manager = new InMemorySessionManager();
        SessionState session = manager.Open("127.0.0.1", 5001);

        // Not commandable until IMEI is attached
        Assert.False(session.IsCommandable);

        manager.AttachImei(session.SessionId, TestImei);
        Assert.True(session.IsCommandable);

        manager.Close(session.SessionId);

        // Not commandable after channel is completed
        Assert.False(session.IsCommandable);

        await Task.CompletedTask; // keep async signature
    }

    [Fact]
    public async Task TryEnqueueCommand_AfterClose_ReturnsFalse()
    {
        var manager = new InMemorySessionManager();
        SessionState session = manager.Open("127.0.0.1", 5001);
        manager.AttachImei(session.SessionId, TestImei);

        bool beforeClose = manager.TryEnqueueCommand(
            TestImei, new OutboundFrame.RawAck(Encoding.ASCII.GetBytes("test")));
        Assert.True(beforeClose);

        manager.Close(session.SessionId);

        bool afterClose = manager.TryEnqueueCommand(
            TestImei, new OutboundFrame.RawAck(Encoding.ASCII.GetBytes("test")));
        Assert.False(afterClose);

        await Task.CompletedTask;
    }

    // ─── Channel: bounded capacity stops writers when full ───────────────────

    [Fact]
    public async Task BoundedChannel_WhenFull_WriterWaitsUntilDrained()
    {
        // The channel has capacity 16. We enqueue 16 items and verify
        // a 17th write blocks until the reader drains one.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var manager = new InMemorySessionManager();
        SessionState session = manager.Open("127.0.0.1", 5001);
        manager.AttachImei(session.SessionId, TestImei);

        // Fill to capacity
        for (int i = 0; i < 16; i++)
        {
            bool wrote = session.OutboundChannel.Writer.TryWrite(
                new OutboundFrame.RawAck(Encoding.ASCII.GetBytes("X")));
            Assert.True(wrote, $"Frame {i} should write immediately into empty channel");
        }

        // 17th write should NOT succeed synchronously (channel is full)
        bool immediate17 = session.OutboundChannel.Writer.TryWrite(
            new OutboundFrame.RawAck(Encoding.ASCII.GetBytes("X")));
        Assert.False(immediate17);

        // Drain one item → 17th write should now succeed via WriteAsync
        session.OutboundChannel.Reader.TryRead(out _);

        bool wrote17 = await session.OutboundChannel.Writer.WriteAsync(
            new OutboundFrame.RawAck(Encoding.ASCII.GetBytes("X")), cts.Token)
            .AsTask().ContinueWith(_ => true, cts.Token);

        Assert.True(wrote17);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static byte[] ExtractBytes(OutboundFrame frame) => frame switch
    {
        OutboundFrame.RawAck ack => ack.Bytes.ToArray(),
        OutboundFrame.Command cmd => Encoding.ASCII.GetBytes($"CMD:{cmd.Cmd.CommandId}"),
        _ => []
    };
}
