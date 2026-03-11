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

    [Fact]
    public void CantrackTracking_ShouldParseTelemetryFields()
    {
        var parser = new CantrackProtocolParser();
        string payload = "*HQ,359586015829802,V1,250301,123045,A,2234.1234,N,11354.1234,E,60,180#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out string? error);

        Assert.True(parsedOk);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(MessageType.Tracking, message!.MessageType);
        Assert.Equal("359586015829802", message.Imei);
        Assert.True(message.IsTelemetryUsable);
        Assert.Null(message.TelemetryError);
        Assert.NotNull(message.GpsTimeUtc);
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 12, 30, 45, TimeSpan.Zero), message.GpsTimeUtc!.Value);
        Assert.NotNull(message.Latitude);
        Assert.NotNull(message.Longitude);
        Assert.Equal(22.568723d, message.Latitude!.Value, 6);
        Assert.Equal(113.902057d, message.Longitude!.Value, 6);
        Assert.Equal(60d, message.SpeedKmh!.Value, 6);
        Assert.Equal(180, message.HeadingDeg!.Value);
    }

    [Fact]
    public void CantrackTrackingInvalid_ShouldRemainTrackingButMarkTelemetryAsInvalid()
    {
        var parser = new CantrackProtocolParser();
        string payload = "*HQ,359586015829802,V1,250301,123045,A,2234.1234,Q,11354.1234,E,60,180#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out string? error);

        Assert.True(parsedOk);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(MessageType.Tracking, message!.MessageType);
        Assert.False(message.IsTelemetryUsable);
        Assert.Equal("invalid_hemisphere", message.TelemetryError);
        Assert.Null(message.Latitude);
        Assert.Null(message.Longitude);
    }
}
