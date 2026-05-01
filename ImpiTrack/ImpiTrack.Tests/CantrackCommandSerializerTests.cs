using System.Text;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Cantrack;

namespace ImpiTrack.Tests;

public sealed class CantrackCommandSerializerTests
{
    private const string TestImei = "359586015829802";
    private readonly CantrackCommandSerializer _sut = new();

    // ─── Supports ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DeviceCommandType.Arm, true)]
    [InlineData(DeviceCommandType.Disarm, true)]
    [InlineData(DeviceCommandType.CutMotor, true)]
    [InlineData(DeviceCommandType.RestoreMotor, true)]
    [InlineData(DeviceCommandType.SetMovementAlarm, true)]
    [InlineData(DeviceCommandType.CancelMovementAlarm, true)]
    [InlineData(DeviceCommandType.CancelAlarm, true)]
    [InlineData(DeviceCommandType.Reset, true)]
    [InlineData(DeviceCommandType.SetOverspeedAlarm, false)]
    [InlineData(DeviceCommandType.SetGeofence, false)]
    [InlineData(DeviceCommandType.CancelGeofence, false)]
    [InlineData(DeviceCommandType.RequestSinglePosition, false)]
    public void Supports_ReturnsExpected(DeviceCommandType type, bool expected)
    {
        Assert.Equal(expected, _sut.Supports(type));
    }

    // ─── Serialize: wire format ───────────────────────────────────────────────

    [Theory]
    [InlineData(DeviceCommandType.Arm, "arm654321")]
    [InlineData(DeviceCommandType.Disarm, "disarm654321")]
    [InlineData(DeviceCommandType.CutMotor, "stop654321")]
    [InlineData(DeviceCommandType.RestoreMotor, "resume654321")]
    [InlineData(DeviceCommandType.CancelMovementAlarm, "nomove654321")]
    [InlineData(DeviceCommandType.Reset, "reset654321")]
    public void Serialize_SimpleCommands_ProducesCorrectFormat(DeviceCommandType type, string expectedBodySegment)
    {
        DeviceCommand cmd = BuildCommand(type);

        string wire = Encoding.ASCII.GetString(_sut.Serialize(cmd).Span);

        // Format: *HQ,{IMEI},CMD,{hhmmss},{body}#
        Assert.StartsWith($"*HQ,{TestImei},CMD,", wire);
        Assert.EndsWith($",{expectedBodySegment}#", wire);
    }

    [Fact]
    public void Serialize_CancelAlarm_HasPasswordAndZero()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.CancelAlarm);

        string wire = Encoding.ASCII.GetString(_sut.Serialize(cmd).Span);

        Assert.EndsWith(",KC654321 0#", wire);
    }

    [Fact]
    public void Serialize_SetMovementAlarm_PadsRadiusTo4Digits()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetMovementAlarm,
            new Dictionary<string, object> { ["radius"] = "50" });

        string wire = Encoding.ASCII.GetString(_sut.Serialize(cmd).Span);

        Assert.EndsWith(",move654321 0050#", wire);
    }

    [Fact]
    public void Serialize_SetMovementAlarm_MissingRadius_Throws()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetMovementAlarm);

        Assert.Throws<CommandParameterMissingException>(() => _sut.Serialize(cmd));
    }

    [Fact]
    public void Serialize_UnsupportedCommand_Throws()
    {
        DeviceCommand cmd = BuildCommand(DeviceCommandType.SetOverspeedAlarm);

        Assert.Throws<CommandNotSupportedByProtocolException>(() => _sut.Serialize(cmd));
    }

    // ─── CRITICAL: hhmmss uniqueness / dedup logic ───────────────────────────

    [Fact]
    public void Serialize_FiveConsecutiveSameImei_AllHhmmssDistinct()
    {
        // Fire 5 commands for the same IMEI in rapid succession.
        // The dedup logic must ensure every hhmmss is distinct.
        const string imei = "111222333444555";
        var serializer = new CantrackCommandSerializer();

        var hhmmssValues = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            DeviceCommand cmd = new(Guid.NewGuid(), imei, DeviceCommandType.Arm, null, DateTimeOffset.UtcNow);
            string wire = Encoding.ASCII.GetString(serializer.Serialize(cmd).Span);
            // Format: *HQ,IMEI,CMD,{hhmmss},{body}#
            string[] parts = wire.TrimEnd('#').TrimStart('*').Split(',');
            Assert.True(parts.Length >= 5, $"Unexpected wire format: {wire}");
            hhmmssValues.Add(parts[3]); // hhmmss is parts[3]
        }

        // All 5 must be unique
        Assert.Equal(5, hhmmssValues.Distinct().Count());
    }

    [Fact]
    public void Serialize_DifferentImeis_DoNotCauseCollision()
    {
        // Two different IMEIs can share hhmmss — only same IMEI is deduplicated
        var s1 = new CantrackCommandSerializer();
        var s2 = new CantrackCommandSerializer();

        DeviceCommand cmd1 = new(Guid.NewGuid(), "IMEI_A", DeviceCommandType.Arm, null, DateTimeOffset.UtcNow);
        DeviceCommand cmd2 = new(Guid.NewGuid(), "IMEI_B", DeviceCommandType.Arm, null, DateTimeOffset.UtcNow);

        // Should not throw
        _ = s1.Serialize(cmd1);
        _ = s2.Serialize(cmd2);
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
