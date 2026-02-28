using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Coban;

namespace ImpiTrack.Tests;

public sealed class CobanProtocolTests
{
    [Fact]
    public void CobanLogin_ShouldParseAndReturnLoadAck()
    {
        var parser = new CobanProtocolParser();
        var ack = new CobanAckStrategy();
        var frame = new Frame(
            Encoding.ASCII.GetBytes("##,imei:359586015829802,A;"),
            DateTimeOffset.UtcNow);

        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out string? error);
        bool ackOk = ack.TryBuildAck(message!, out ReadOnlyMemory<byte> ackBytes);

        Assert.True(parsedOk);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(MessageType.Login, message!.MessageType);
        Assert.Equal("359586015829802", message.Imei);
        Assert.True(ackOk);
        Assert.Equal("LOAD", Encoding.ASCII.GetString(ackBytes.Span));
    }

    [Fact]
    public void CobanTracker_ShouldParseAndReturnOnAck()
    {
        var parser = new CobanProtocolParser();
        var ack = new CobanAckStrategy();
        var frame = new Frame(
            Encoding.ASCII.GetBytes("imei:864035051929066,tracker,250208125816,,F,175816.000,A,0228.81052,N,07634.01441,W,,;"),
            DateTimeOffset.UtcNow);

        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out string? error);
        bool ackOk = ack.TryBuildAck(message!, out ReadOnlyMemory<byte> ackBytes);

        Assert.True(parsedOk);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(MessageType.Tracking, message!.MessageType);
        Assert.Equal("864035051929066", message.Imei);
        Assert.True(ackOk);
        Assert.Equal("ON\r\n", Encoding.ASCII.GetString(ackBytes.Span));
    }
}
