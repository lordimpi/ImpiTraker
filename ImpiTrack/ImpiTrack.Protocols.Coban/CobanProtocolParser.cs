using System.Text;
using System.Globalization;
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
        DateTimeOffset? gpsTimeUtc = null;
        double? latitude = null;
        double? longitude = null;
        double? speedKmh = null;
        int? headingDeg = null;
        bool isTelemetryUsable = true;
        string? telemetryError = null;
        if (type == MessageType.Tracking)
        {
            isTelemetryUsable = TryParseTrackingTelemetry(
                text,
                out gpsTimeUtc,
                out latitude,
                out longitude,
                out speedKmh,
                out headingDeg,
                out telemetryError);
        }

        message = new ParsedMessage(
            Protocol,
            type,
            imei,
            frame.Payload,
            text,
            frame.ReceivedAtUtc,
            gpsTimeUtc,
            latitude,
            longitude,
            speedKmh,
            headingDeg,
            isTelemetryUsable,
            telemetryError);

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

    private static bool TryParseTrackingTelemetry(
        string text,
        out DateTimeOffset? gpsTimeUtc,
        out double? latitude,
        out double? longitude,
        out double? speedKmh,
        out int? headingDeg,
        out string? telemetryError)
    {
        gpsTimeUtc = null;
        latitude = null;
        longitude = null;
        speedKmh = null;
        headingDeg = null;
        telemetryError = null;

        string[] fields = text.Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length < 11 || !fields[1].Equals("tracker", StringComparison.OrdinalIgnoreCase))
        {
            telemetryError = "invalid_tracking_field_count";
            return false;
        }

        if (fields[2].Length == 12 &&
            DateTimeOffset.TryParseExact(
                fields[2],
                "yyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsedGpsTime))
        {
            gpsTimeUtc = parsedGpsTime;
        }

        if (!TryParseCoordinate(fields[7], fields[8], true, out double lat, out telemetryError))
        {
            return false;
        }
        latitude = lat;

        if (!TryParseCoordinate(fields[9], fields[10], false, out double lon, out telemetryError))
        {
            return false;
        }
        longitude = lon;

        if (fields.Length > 11 &&
            double.TryParse(fields[11], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedSpeedKmh))
        {
            speedKmh = parsedSpeedKmh;
        }

        if (fields.Length > 12 &&
            int.TryParse(fields[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHeadingDeg))
        {
            headingDeg = parsedHeadingDeg;
        }

        return true;
    }

    private static bool TryParseCoordinate(
        string rawValue,
        string hemisphere,
        bool isLatitude,
        out double coordinate,
        out string? error)
    {
        coordinate = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            error = isLatitude ? "invalid_latitude" : "invalid_longitude";
            return false;
        }

        if (string.IsNullOrWhiteSpace(hemisphere))
        {
            error = "invalid_hemisphere";
            return false;
        }

        ReadOnlySpan<char> rawSpan = rawValue.Trim().AsSpan();
        int decimalSeparatorIndex = rawSpan.IndexOf('.');
        int minimumWholeDigits = isLatitude ? 4 : 5;
        if (decimalSeparatorIndex < minimumWholeDigits)
        {
            error = isLatitude ? "invalid_latitude" : "invalid_longitude";
            return false;
        }

        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double nmea))
        {
            error = "invalid_coordinate_format";
            return false;
        }

        double degrees = Math.Floor(nmea / 100d);
        double minutes = nmea - (degrees * 100d);
        double decimalDegrees = degrees + (minutes / 60d);

        char hemi = char.ToUpperInvariant(hemisphere[0]);
        bool validHemisphere = isLatitude
            ? hemi is 'N' or 'S'
            : hemi is 'E' or 'W';
        if (!validHemisphere)
        {
            error = "invalid_hemisphere";
            return false;
        }

        if (hemi is 'S' or 'W')
        {
            decimalDegrees *= -1d;
        }

        if (isLatitude && (decimalDegrees < -90d || decimalDegrees > 90d))
        {
            error = "invalid_latitude";
            return false;
        }

        if (!isLatitude && (decimalDegrees < -180d || decimalDegrees > 180d))
        {
            error = "invalid_longitude";
            return false;
        }

        coordinate = decimalDegrees;
        return true;
    }

    [GeneratedRegex(@"imei:(\d{8,20})", RegexOptions.IgnoreCase)]
    private static partial Regex ImeiRegex();

    [GeneratedRegex(@"^\d{8,20}$", RegexOptions.Compiled)]
    private static partial Regex DigitsOnlyRegex();
}
