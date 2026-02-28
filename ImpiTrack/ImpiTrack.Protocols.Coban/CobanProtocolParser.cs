using System.Text;
using System.Text.RegularExpressions;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Coban;

/// <summary>
/// Parser minimo de Coban para paquetes de login, heartbeat y tracker.
/// </summary>
public sealed partial class CobanProtocolParser : IProtocolParser
{
    /// <inheritdoc />
    public ProtocolId Protocol => ProtocolId.Coban;

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

        MessageType type = ResolveType(text);
        if (type == MessageType.Unknown)
        {
            error = "unsupported_coban_message";
            return false;
        }

        string? imei = ParseImei(text);
        message = new ParsedMessage(
            Protocol,
            type,
            imei,
            frame.Payload,
            text,
            frame.ReceivedAtUtc);

        return true;
    }

    private static MessageType ResolveType(string text)
    {
        if (text.StartsWith("##", StringComparison.Ordinal))
        {
            return MessageType.Login;
        }

        if (text.Contains(",tracker,", StringComparison.OrdinalIgnoreCase))
        {
            return MessageType.Tracking;
        }

        if (text.Contains("imei:", StringComparison.OrdinalIgnoreCase))
        {
            return MessageType.Heartbeat;
        }

        if (DigitsOnlyRegex().IsMatch(text))
        {
            return MessageType.Heartbeat;
        }

        return MessageType.Unknown;
    }

    private static string? ParseImei(string text)
    {
        Match match = ImeiRegex().Match(text);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        Match digits = DigitsOnlyRegex().Match(text);
        return digits.Success ? digits.Value : null;
    }

    [GeneratedRegex(@"imei:(\d{8,20})", RegexOptions.IgnoreCase)]
    private static partial Regex ImeiRegex();

    [GeneratedRegex(@"^\d{8,20}$", RegexOptions.Compiled)]
    private static partial Regex DigitsOnlyRegex();
}
