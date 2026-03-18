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
        bool? ignitionOn = null;
        bool? powerConnected = null;
        if (type == MessageType.Tracking)
        {
            isTelemetryUsable = TryParseTrackingTelemetry(
                text,
                out gpsTimeUtc,
                out latitude,
                out longitude,
                out speedKmh,
                out headingDeg,
                out telemetryError,
                out ignitionOn,
                out powerConnected);
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
            telemetryError,
            ignitionOn,
            powerConnected);

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

        // "acc on" and "acc off" are ignition-state event packets — treated as Tracking
        // so they go through the full telemetry pipeline including position parsing.
        if (text.Contains(",acc on,", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(",acc off,", StringComparison.OrdinalIgnoreCase))
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
        out string? telemetryError,
        out bool? ignitionOn,
        out bool? powerConnected)
    {
        gpsTimeUtc = null;
        latitude = null;
        longitude = null;
        speedKmh = null;
        headingDeg = null;
        telemetryError = null;
        ignitionOn = null;
        powerConnected = null;

        string[] fields = text.Split(',', StringSplitOptions.TrimEntries);
        // Accept "tracker", "acc on", and "acc off" as valid tracking message types
        bool isTrackerType = fields.Length >= 2 &&
            (fields[1].Equals("tracker", StringComparison.OrdinalIgnoreCase) ||
             fields[1].Equals("acc on", StringComparison.OrdinalIgnoreCase) ||
             fields[1].Equals("acc off", StringComparison.OrdinalIgnoreCase));

        if (fields.Length < 11 || !isTrackerType)
        {
            telemetryError = "invalid_tracking_field_count";
            return false;
        }

        // GpsTimeUtc: date portion from field[2] (YYMMDD, device local time) + time portion
        // from field[5] (HHMMSS.sss, already UTC per NMEA standard).
        // field[2] full value is local time YYMMDDHHMMSS — we only use the date part.
        // field[5] format: HHMMSS.sss (GPS satellite UTC, Colombia UTC-5).
        // Day rollover: when local time is 23:xx and UTC crosses midnight to 04:xx next day,
        // we detect this by comparing the UTC hour from field[5] against the local hour
        // from field[2] — if UTC hour < local hour we add one day.
        if (fields.Length > 5 &&
            fields[2].Length == 12 &&
            fields[5].Length >= 6 &&
            int.TryParse(fields[2].AsSpan(0, 2), out int yy) &&
            int.TryParse(fields[2].AsSpan(2, 2), out int mm) &&
            int.TryParse(fields[2].AsSpan(4, 2), out int dd) &&
            int.TryParse(fields[2].AsSpan(6, 2), out int localHour) &&
            int.TryParse(fields[5].AsSpan(0, 2), out int utcHour) &&
            int.TryParse(fields[5].AsSpan(2, 2), out int utcMin) &&
            int.TryParse(fields[5].AsSpan(4, 2), out int utcSec))
        {
            int year = 2000 + yy;
            // Day rollover: UTC hour is less than local hour → UTC has crossed midnight
            int dayOffset = utcHour < localHour ? 1 : 0;
            try
            {
                var localDate = new DateTime(year, mm, dd, 0, 0, 0, DateTimeKind.Unspecified);
                DateTime utcDateTime = localDate.AddDays(dayOffset)
                    .AddHours(utcHour)
                    .AddMinutes(utcMin)
                    .AddSeconds(utcSec);
                gpsTimeUtc = new DateTimeOffset(utcDateTime, TimeSpan.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid date components — leave gpsTimeUtc null
            }
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

        // B.3: IgnitionOn from field[1] message type and field[14] ACC bit.
        // "acc on" message type → ignition ON regardless of field[14].
        // "acc off" message type → ignition OFF.
        // field[14] == "1" → true, "0" → false, missing or unparseable → null.
        if (fields[1].Equals("acc on", StringComparison.OrdinalIgnoreCase))
        {
            ignitionOn = true;
        }
        else if (fields[1].Equals("acc off", StringComparison.OrdinalIgnoreCase))
        {
            ignitionOn = false;
        }
        else if (fields.Length > 14 && !string.IsNullOrEmpty(fields[14]))
        {
            ignitionOn = fields[14] == "1";
        }

        // B.4: PowerConnected heuristic from field[3] battery %.
        // field[3] format: "100%", "0%", etc. Non-empty and > 0% → device has external power.
        // This is a best-effort heuristic — real PWR field index confirmed as field[15] but
        // the exact values for PWR_ON/PWR_OFF in that field are NOT yet confirmed.
        if (fields.Length > 3 && fields[3].EndsWith('%'))
        {
            ReadOnlySpan<char> batterySpan = fields[3].AsSpan(0, fields[3].Length - 1);
            if (int.TryParse(batterySpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int batteryPct))
            {
                powerConnected = batteryPct > 0;
            }
        }

        // TODO B.12: field[15] index for Door/PWR NOT confirmed against a real Coban packet with
        // door-open or battery-disconnect event. DoorOpen is left null until a real packet confirms
        // the field index and the expected values (e.g. "1"/"0" or other encoding).
        // DO NOT parse field[15] until that validation is complete.

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
