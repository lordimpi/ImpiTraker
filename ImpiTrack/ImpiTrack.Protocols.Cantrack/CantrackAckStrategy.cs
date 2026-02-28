using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Cantrack;

/// <summary>
/// Usa eco del payload crudo como ACK para paquetes Cantrack.
/// </summary>
public sealed class CantrackAckStrategy : IAckStrategy
{
    /// <inheritdoc />
    public ProtocolId Protocol => ProtocolId.Cantrack;

    /// <inheritdoc />
    public bool TryBuildAck(in ParsedMessage message, out ReadOnlyMemory<byte> ackBytes)
    {
        ackBytes = default;
        if (message.Protocol != Protocol)
        {
            return false;
        }

        if (message.RawPayload.IsEmpty)
        {
            return false;
        }

        ackBytes = message.RawPayload;
        return true;
    }
}
