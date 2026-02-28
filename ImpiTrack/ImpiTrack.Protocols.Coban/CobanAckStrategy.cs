using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Coban;

/// <summary>
/// Construye payloads ACK de Coban segun el tipo de mensaje.
/// </summary>
public sealed class CobanAckStrategy : IAckStrategy
{
    /// <inheritdoc />
    public ProtocolId Protocol => ProtocolId.Coban;

    /// <inheritdoc />
    public bool TryBuildAck(in ParsedMessage message, out ReadOnlyMemory<byte> ackBytes)
    {
        ackBytes = default;
        if (message.Protocol != Protocol)
        {
            return false;
        }

        string ack = message.MessageType switch
        {
            MessageType.Login => "LOAD",
            MessageType.Heartbeat => "ON\r\n",
            MessageType.Tracking => "ON\r\n",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(ack))
        {
            return false;
        }

        ackBytes = Encoding.ASCII.GetBytes(ack);
        return true;
    }
}
