using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Cantrack;

namespace ImpiTrack.Tests;

public sealed class CantrackProtocolTests
{
    [Fact]
    public void CantrackLoginV0_ShouldParseAndEchoAck()
    {
        var parser = new CantrackProtocolParser();
        var ack = new CantrackAckStrategy();
        string payload = "*HQ,359586015829802,V0#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out string? error);
        bool ackOk = ack.TryBuildAck(message!, out ReadOnlyMemory<byte> ackBytes);

        Assert.True(parsedOk);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(MessageType.Login, message!.MessageType);
        Assert.Equal("359586015829802", message.Imei);
        Assert.True(ackOk);
        Assert.Equal(payload, Encoding.ASCII.GetString(ackBytes.Span));
    }

    [Fact]
    public void CantrackHeartbeat_ShouldParseAsHeartbeat()
    {
        var parser = new CantrackProtocolParser();
        string payload = "*HQ,359586015829802,HTBT,100#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out _);

        Assert.True(parsedOk);
        Assert.NotNull(message);
        Assert.Equal(MessageType.Heartbeat, message!.MessageType);
    }
}
