using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Coban;

namespace ImpiTrack.Tests;

public sealed class CobanCommandSerializerTests
{
    private const string TestImei = "123456789012345";
    private readonly CobanCommandSerializer _sut = new();

    // ─── Supports ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DeviceCommandType.Arm, true)]
    [InlineData(DeviceCommandType.Disarm, true)]
    [InlineData(DeviceCommandType.CutMotor, true)]
    [InlineData(DeviceCommandType.RestoreMotor, true)]
    [InlineData(DeviceCommandType.SetMovementAlarm, true)]
    [InlineData(DeviceCommandType.CancelMovementAlarm, true)]
    [InlineData(DeviceCommandType.SetOverspeedAlarm, true)]
    [InlineData(DeviceCommandType.SetGeofence, true)]
    [InlineData(DeviceCommandType.CancelGeofence, true)]
    [InlineData(DeviceCommandType.CancelAlarm, true)]
    [InlineData(DeviceCommandType.RequestSinglePosition, true)]
    [InlineData(DeviceCommandType.Reset, false)]
    public void Supports_ReturnsExpected(DeviceCommandType type, bool expected)
    {
        Assert.Equal(expected, _sut.Supports(type));
    }

    // ─── Serialize: simple keyword commands ──────────────────────────────────

    [Theory]
    [InlineData(DeviceCommandType.Arm, "**,imei:123456789012345,111\r\n")]
    [InlineData(DeviceCommandType.Disarm, "**,imei:123456789012345,112\r\n")]
    [InlineData(DeviceCommandType.CutMotor, "**,imei:123456789012345,109\r\n")]
    [InlineData(DeviceCommandType.RestoreMotor, "**,imei:123456789012345,110\r\n")]
    [InlineData(DeviceCommandType.CancelMovementAlarm, "**,imei:123456789012345,106\r\n")]
    [InlineData(DeviceCommandType.CancelGeofence, "**,imei:123456789012345,115\r\n")]
    [InlineData(DeviceCommandType.CancelAlarm, "**,imei:123456789012345,104\r\n")]
    [InlineData(DeviceCommandType.RequestSinglePosition, "**,imei:123456789012345,100\r\n")]
    public void Serialize_SimpleCommands_ProducesExactWireFormat(DeviceCommandType type, string expected)
    {
        DeviceCommand cmd = BuildCommand(type);

        ReadOnlyMemory<byte> result = _sut.Serialize(cmd);

        Assert.Equal(expected, Encoding.ASCII.GetString(result.Span));
    }

    [Fact]
    public void Serialize_SetMovementAlarm_PadsRadiusTo5Digits()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetMovementAlarm,
            new Dictionary<string, object> { ["radius"] = "200" });

        ReadOnlyMemory<byte> result = _sut.Serialize(cmd);

        Assert.Equal("**,imei:123456789012345,105,00200\r\n", Encoding.ASCII.GetString(result.Span));
    }

    [Fact]
    public void Serialize_SetOverspeedAlarm_PadsSpeedTo3Digits()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetOverspeedAlarm,
            new Dictionary<string, object> { ["speed"] = "80" });

        ReadOnlyMemory<byte> result = _sut.Serialize(cmd);

        Assert.Equal("**,imei:123456789012345,107,080\r\n", Encoding.ASCII.GetString(result.Span));
    }

    [Fact]
    public void Serialize_SetGeofence_ProducesCorrectWireFormat()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetGeofence,
            new Dictionary<string, object>
            {
                ["latTL"] = "4.7",
                ["lonTL"] = "-74.1",
                ["latBR"] = "4.6",
                ["lonBR"] = "-74.0"
            });

        ReadOnlyMemory<byte> result = _sut.Serialize(cmd);

        Assert.Equal("**,imei:123456789012345,114,4.7,-74.1;4.6,-74.0\r\n",
            Encoding.ASCII.GetString(result.Span));
    }

    // ─── Serialize: unsupported throws ───────────────────────────────────────

    [Fact]
    public void Serialize_Reset_ThrowsCommandNotSupported()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.Reset);

        Assert.Throws<CommandNotSupportedByProtocolException>(() => _sut.Serialize(cmd));
    }

    // ─── Serialize: missing param throws ─────────────────────────────────────

    [Fact]
    public void Serialize_SetMovementAlarm_MissingRadius_Throws()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetMovementAlarm);

        Assert.Throws<CommandParameterMissingException>(() => _sut.Serialize(cmd));
    }

    [Fact]
    public void Serialize_SetOverspeedAlarm_MissingSpeed_Throws()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetOverspeedAlarm);

        Assert.Throws<CommandParameterMissingException>(() => _sut.Serialize(cmd));
    }

    [Fact]
    public void Serialize_SetGeofence_MissingLatTL_Throws()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetGeofence,
            new Dictionary<string, object> { ["lonTL"] = "1", ["latBR"] = "1", ["lonBR"] = "1" });

        Assert.Throws<CommandParameterMissingException>(() => _sut.Serialize(cmd));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DeviceCommand BuildCommand(
        DeviceCommandType type,
        Dictionary<string, object>? rawParams = null)
    {
        System.Text.Json.Nodes.JsonObject? jsonParams = null;
        if (rawParams is not null)
        {
            jsonParams = new System.Text.Json.Nodes.JsonObject();
            foreach (var kv in rawParams)
            {
                jsonParams[kv.Key] = kv.Value?.ToString();
            }
        }

        return new DeviceCommand(Guid.NewGuid(), TestImei, type, jsonParams, DateTimeOffset.UtcNow);
    }
}
