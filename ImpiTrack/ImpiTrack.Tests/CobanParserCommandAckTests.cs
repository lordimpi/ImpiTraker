using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Coban;

namespace ImpiTrack.Tests;

/// <summary>
/// Tests for CommandAck detection in CobanProtocolParser.
/// Covers all known ACK codes from the protocol manual, forward-compat with unknown codes,
/// and verifies that existing login/heartbeat/tracker paths still work.
/// </summary>
public sealed class CobanParserCommandAckTests
{
    private readonly CobanProtocolParser _parser = new();

    // ─── Known ACK codes → CommandAck ────────────────────────────────────────

    [Theory]
    [InlineData("001")]
    [InlineData("100")]
    [InlineData("102")]
    [InlineData("104")]
    [InlineData("105")]
    [InlineData("106")]
    [InlineData("107")]
    [InlineData("108")]
    [InlineData("109")]
    [InlineData("110")]
    [InlineData("111")]
    [InlineData("112")]
    [InlineData("113")]
    [InlineData("114")]
    [InlineData("115")]
    [InlineData("116")]
    [InlineData("117")]
    [InlineData("118")]
    [InlineData("119")]
    [InlineData("120")]
    [InlineData("121")]
    [InlineData("122")]
    [InlineData("123")]
    [InlineData("124")]
    [InlineData("125")]
    [InlineData("150")]
    [InlineData("151")]
    [InlineData("152")]
    [InlineData("509")]
    [InlineData("511")]
    [InlineData("525")]
    [InlineData("526")]
    public void KnownAckCode_ParsesAsCommandAck_WithResponseCode(string code)
    {
        string payload = $"imei:864035051929066,{code};";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.CommandAck, msg!.MessageType);
        Assert.Equal(code, msg.ResponseCode);
        Assert.Equal("864035051929066", msg.Imei);
    }

    [Fact]
    public void UnknownNumericCode_StillParsesAsCommandAck_ForwardCompat()
    {
        // A numeric code not in the known set must still be recognized as CommandAck.
        string payload = "imei:864035051929066,999;";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.CommandAck, msg!.MessageType);
        Assert.Equal("999", msg.ResponseCode);
    }

    [Fact]
    public void AckWithPositionData_PopulatesLatLon()
    {
        // Some Coban ACKs carry position data after the response code.
        // Format mirrors tracker but field[1] is the code.
        string payload = "imei:864035051929066,109,260314230546,,F,040546.000,A,0228.81052,N,07634.01441,W,,;";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.CommandAck, msg!.MessageType);
        Assert.Equal("109", msg.ResponseCode);
        Assert.NotNull(msg.Latitude);
        Assert.NotNull(msg.Longitude);
    }

    // ─── Existing positive paths still work ──────────────────────────────────

    [Fact]
    public void LoginPacket_NotAffectedByAckBranch()
    {
        string payload = "##,imei:359586015829802,A;";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.Login, msg!.MessageType);
    }

    [Fact]
    public void TrackerPacket_NonNumericKeyword_NotParsedAsAck()
    {
        string payload = "imei:864035051929066,tracker,260314230546,,F,040546.000,A,0228.81052,N,07634.01441,W,,;";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        Assert.Equal(MessageType.Tracking, msg!.MessageType);
        Assert.Null(msg.ResponseCode);
    }

    [Fact]
    public void HeartbeatPacket_NotParsedAsAck()
    {
        // Coban heartbeat: packet containing "imei:" but non-numeric non-tracker field
        string payload = "imei:864035051929066,heartbeat;";
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool ok = _parser.TryParse(frame, out ParsedMessage? msg, out _);

        Assert.True(ok);
        Assert.NotNull(msg);
        // Should be Heartbeat or Unknown — NOT CommandAck
        Assert.NotEqual(MessageType.CommandAck, msg!.MessageType);
    }
}
