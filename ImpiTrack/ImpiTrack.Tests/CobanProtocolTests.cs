using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Coban;

namespace ImpiTrack.Tests;

public sealed class CobanProtocolTests
{
    [Fact]
    public void CobanParser_GpsTimeUtc_UsesField5NotField2()
    {
        // Arrange
        // Real Coban packet structure:
        //   field[2] = "260314230546" → local time YYMMDDHHMMSS (Colombia UTC-5): 2026-03-14 23:05:46 local
        //   field[5] = "040546.000"   → GPS UTC time HHMMSS.sss: 04:05:46 UTC
        // Because local time is 23:xx and UTC is 04:xx, the UTC date is the NEXT day: 2026-03-15
        // If the parser naively treated field[2] as UTC it would produce 2026-03-14 23:05:46 UTC — wrong by 5 hours.
        var parser = new CobanProtocolParser();
        var frame = new Frame(
            Encoding.ASCII.GetBytes("imei:864035051929066,tracker,260314230546,,F,040546.000,A,0228.81052,N,07634.01441,W,,;"),
            DateTimeOffset.UtcNow);

        // Act
        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out string? error);

        // Assert
        Assert.True(parsedOk);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.True(message!.IsTelemetryUsable);
        Assert.NotNull(message.GpsTimeUtc);

        // GpsTimeUtc must be 2026-03-15 04:05:46 UTC (field[5] UTC time, next day due to midnight rollover)
        // NOT 2026-03-14 23:05:46 UTC (which would be the wrong result from field[2] treated as UTC)
        var expected = new DateTimeOffset(2026, 3, 15, 4, 5, 46, TimeSpan.Zero);
        Assert.Equal(expected, message.GpsTimeUtc!.Value);
    }


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
        Assert.True(message.IsTelemetryUsable);
        Assert.Null(message.TelemetryError);
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
        Assert.True(message.IsTelemetryUsable);
        Assert.Null(message.TelemetryError);
        Assert.NotNull(message.GpsTimeUtc);
        // field[5] = "175816.000" → UTC 17:58:16 (field[2] "125816" is local Colombia time, not UTC)
        Assert.Equal(new DateTimeOffset(2025, 2, 8, 17, 58, 16, TimeSpan.Zero), message.GpsTimeUtc!.Value);
        Assert.NotNull(message.Latitude);
        Assert.NotNull(message.Longitude);
        Assert.Equal(2.480175d, message.Latitude!.Value, 6);
        Assert.Equal(-76.566907d, message.Longitude!.Value, 6);
        Assert.True(ackOk);
        Assert.Equal("ON\r\n", Encoding.ASCII.GetString(ackBytes.Span));
    }

    [Fact]
    public void CobanParser_AccField14On_IgnitionOnTrue()
    {
        // Arrange
        // Real Coban packet with field[14] = "1" (ACC ON).
        // Packet: imei:...,tracker,timestamp,battery,F,gpstime,A,lat,N,lon,W,speed,heading,altitude,1,field15,mileage
        // field[0]=imei, [1]=tracker, [2]=ts, [3]=100%, [4]=F, [5]=gpstime, [6]=A,
        // [7]=lat, [8]=N, [9]=lon, [10]=W, [11]=speed, [12]=heading, [13]=altitude(empty), [14]=1
        var parser = new CobanProtocolParser();
        var frame = new Frame(
            Encoding.ASCII.GetBytes("imei:864035053671278,tracker,260314230546,100%,F,040546.000,A,0227.34852,N,07635.46362,W,12.29,155.12,,1,0,0.00%;"),
            DateTimeOffset.UtcNow);

        // Act
        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out _);

        // Assert
        Assert.True(parsedOk);
        Assert.NotNull(message);
        Assert.True(message!.IsTelemetryUsable);
        Assert.True(message.IgnitionOn, "field[14]='1' debe resultar en IgnitionOn=true");
    }

    [Fact]
    public void CobanParser_AccField14Off_IgnitionOnFalse()
    {
        // Arrange — same packet structure but field[14] = "0"
        var parser = new CobanProtocolParser();
        var frame = new Frame(
            Encoding.ASCII.GetBytes("imei:864035053671278,tracker,260314230546,100%,F,040546.000,A,0227.34852,N,07635.46362,W,12.29,155.12,,0,0,0.00%;"),
            DateTimeOffset.UtcNow);

        // Act
        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out _);

        // Assert
        Assert.True(parsedOk);
        Assert.NotNull(message);
        Assert.True(message!.IsTelemetryUsable);
        Assert.False(message.IgnitionOn, "field[14]='0' debe resultar en IgnitionOn=false");
    }

    [Fact]
    public void CobanParser_AccOnMessageType_IgnitionOnTrue()
    {
        // Arrange — field[1] = "acc on" is a Coban ignition-ON event packet.
        // The message type in field[1] overrides whatever field[14] contains.
        var parser = new CobanProtocolParser();
        var frame = new Frame(
            Encoding.ASCII.GetBytes("imei:864035053671278,acc on,260314230546,100%,F,040546.000,A,0227.34852,N,07635.46362,W,0.00,0,,0,0,0.00%;"),
            DateTimeOffset.UtcNow);

        // Act
        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out _);

        // Assert
        Assert.True(parsedOk);
        Assert.NotNull(message);
        Assert.Equal(MessageType.Tracking, message!.MessageType);
        Assert.True(message.IgnitionOn, "field[1]='acc on' debe forzar IgnitionOn=true sin importar field[14]");
    }

    [Fact]
    public void CobanParser_NoAccField_IgnitionOnNull()
    {
        // Arrange — classic short packet without field[14]
        var parser = new CobanProtocolParser();
        var frame = new Frame(
            Encoding.ASCII.GetBytes("imei:864035051929066,tracker,250208125816,,F,175816.000,A,0228.81052,N,07634.01441,W,,;"),
            DateTimeOffset.UtcNow);

        // Act
        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out _);

        // Assert
        Assert.True(parsedOk);
        Assert.NotNull(message);
        Assert.Null(message!.IgnitionOn);
    }

    [Theory]
    [InlineData("imei:864035051929066,tracker,250208125816,,F,175816.000,A,022A.81052,N,07634.01441,W,,;", "invalid_coordinate_format")]
    [InlineData("imei:864035051929066,tracker,250208125816,,F,175816.000,A,028,N,07634.01441,W,,;", "invalid_latitude")]
    [InlineData("imei:864035051929066,tracker,250208125816,,F,175816.000,A,0228.81052,Q,07634.01441,W,,;", "invalid_hemisphere")]
    public void CobanTrackerInvalid_ShouldRemainTrackingButMarkTelemetryAsInvalid(string payload, string expectedTelemetryError)
    {
        var parser = new CobanProtocolParser();
        var ack = new CobanAckStrategy();
        var frame = new Frame(Encoding.ASCII.GetBytes(payload), DateTimeOffset.UtcNow);

        bool parsedOk = parser.TryParse(frame, out ParsedMessage? message, out string? error);
        bool ackOk = ack.TryBuildAck(message!, out ReadOnlyMemory<byte> ackBytes);

        Assert.True(parsedOk);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(MessageType.Tracking, message!.MessageType);
        Assert.False(message.IsTelemetryUsable);
        Assert.Equal(expectedTelemetryError, message.TelemetryError);
        Assert.Null(message.Latitude);
        Assert.Null(message.Longitude);
        Assert.True(ackOk);
        Assert.Equal("ON\r\n", Encoding.ASCII.GetString(ackBytes.Span));
    }
}
