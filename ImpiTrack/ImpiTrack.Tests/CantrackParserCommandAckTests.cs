using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Cantrack;

namespace ImpiTrack.Tests;

/// <summary>
/// Tests for CommandAck detection in CantrackProtocolParser.
/// Covers V4 structured ACK, plaintext ACK, and preservation of existing V0/V1/V2/V3 paths.
/// </summary>
public sealed class CantrackParserCommandAckTests
{
    private readonly CantrackProtocolParser _parser = new();

    // ─── V4 structured ACK ───────────────────────────────────────────────────

    [Fact]
    public void V4Packet_ParsesAsCommandAck_WithCorrelationFields()
    {
        string payload = "*HQ,359586015829802,V4,stop,143022,A,2234.1234,N,11354.1234,E,0,0,120301#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.CommandAck, msg!.MessageType);
        Assert.Equal("stop", msg.CorrelationKey);
        Assert.Equal("143022", msg.CorrelationTimestamp);
        Assert.Equal("stop", msg.ResponseCode);
        Assert.Equal("359586015829802", msg.Imei);
    }

    [Fact]
    public void V4Packet_WithMinimalFields_ParsesAsCommandAck()
    {
        // V4 with just CMD and hhmmss (no position data)
        string payload = "*HQ,359586015829802,V4,arm,100000#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.CommandAck, msg!.MessageType);
        Assert.Equal("arm", msg.CorrelationKey);
        Assert.Equal("100000", msg.CorrelationTimestamp);
    }

    // ─── Plaintext ACK responses ──────────────────────────────────────────────

    [Fact]
    public void PlaintextStopSucceed_ParsesAsCommandAck()
    {
        string payload = "stop engine succeed";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.CommandAck, msg!.MessageType);
        Assert.Equal("TEXT", msg.ResponseCode);
        Assert.Equal("stop", msg.CorrelationKey); // normalized key
    }

    [Fact]
    public void PlaintextResumeSucceed_ParsesAsCommandAck()
    {
        string payload = "resume engine succeed";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.CommandAck, msg!.MessageType);
        Assert.Equal("resume", msg.CorrelationKey);
    }

    [Fact]
    public void PlaintextArbitraryText_ParsesAsCommandAck_NoImei()
    {
        // Any non-'*' text is treated as a plaintext ACK
        string payload = "some other ack text";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.CommandAck, msg!.MessageType);
        Assert.Null(msg.Imei); // no IMEI in plaintext responses
    }

    // ─── Existing positive paths still work ──────────────────────────────────

    [Fact]
    public void V0Login_StillParsesAsLogin()
    {
        string payload = "*HQ,359586015829802,V0#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.Login, msg!.MessageType);
        Assert.Null(msg.ResponseCode);
    }

    [Fact]
    public void V1Tracking_StillParses_NoAckFields()
    {
        string payload = "*HQ,359586015829802,V1,250301,123045,A,2234.1234,N,11354.1234,E,60,180#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.Tracking, msg!.MessageType);
        Assert.Null(msg.ResponseCode);
        Assert.Null(msg.CorrelationKey);
    }

    [Fact]
    public void HeartbeatHTBT_StillParsesAsHeartbeat()
    {
        string payload = "*HQ,359586015829802,HTBT,100#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.Heartbeat, msg!.MessageType);
    }

    [Fact]
    public void V3Packet_ParsesAsTracking()
    {
        // V3 follows the same V-series tracking shape
        string payload = "*HQ,359586015829802,V3,250301,123045,A,2234.1234,N,11354.1234,E,60,180#";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.Tracking, msg!.MessageType);
    }
}
