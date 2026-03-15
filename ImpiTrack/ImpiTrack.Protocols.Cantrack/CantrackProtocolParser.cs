using System.Text;
using System.Globalization;
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
        string[] parts = core.Split(',', StringSplitOptions.TrimEntries);
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
                parts,
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
            string.IsNullOrWhiteSpace(imei) ? null : imei,
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

    private static bool TryParseTrackingTelemetry(
        IReadOnlyList<string> fields,
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

        if (fields.Count < 10)
        {
            telemetryError = "invalid_tracking_field_count";
            return false;
        }

        if (fields.Count > 4 &&
            fields[3].Length == 6 &&
            fields[4].Length >= 6 &&
            DateTimeOffset.TryParseExact(
                $"{fields[3]}{fields[4][..6]}",
                "yyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsedGpsTime))
        {
            gpsTimeUtc = parsedGpsTime;
        }

        if (!TryParseCoordinate(fields[6], fields[7], true, out double lat, out telemetryError))
        {
            return false;
        }

        if (!TryParseCoordinate(fields[8], fields[9], false, out double lon, out telemetryError))
        {
            return false;
        }

        latitude = lat;
        longitude = lon;

        if (fields.Count > 10 &&
            double.TryParse(fields[10], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedSpeed))
        {
            speedKmh = parsedSpeed;
        }

        if (fields.Count > 11 &&
            int.TryParse(fields[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHeadingDeg))
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
}
