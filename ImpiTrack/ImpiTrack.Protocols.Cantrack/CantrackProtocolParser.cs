using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Cantrack;

/// <summary>
/// Parser minimo de Cantrack para login V0, heartbeat HTBT y paquetes de tracking serie V.
/// </summary>
public sealed class CantrackProtocolParser : IProtocolParser
{
    /// <inheritdoc />
    public ProtocolId Protocol => ProtocolId.Cantrack;

    /// <inheritdoc />
    public bool TryParse(in Frame frame, out ParsedMessage? message, out string? error)
    {
        message = null;
        error = null;

        string text = Encoding.ASCII.GetString(frame.Payload.Span).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty_payload";
            return false;
        }

        if (!text.StartsWith('*'))
        {
            error = "invalid_cantrack_prefix";
            return false;
        }

        int hashIndex = text.IndexOf('#');
        string core = hashIndex >= 0 ? text[..hashIndex] : text;
        string[] parts = core.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            error = "invalid_cantrack_fields";
            return false;
        }

        string imei = parts[1];
        string command = parts[2].ToUpperInvariant();
        MessageType type = command switch
        {
            "V0" => MessageType.Login,
            "HTBT" => MessageType.Heartbeat,
            "V1" => MessageType.Tracking,
            _ when command.StartsWith("V", StringComparison.Ordinal) => MessageType.Tracking,
            _ => MessageType.Unknown
        };

        message = new ParsedMessage(
            Protocol,
            type,
            string.IsNullOrWhiteSpace(imei) ? null : imei,
            frame.Payload,
            text,
            frame.ReceivedAtUtc);

        return true;
    }
}
