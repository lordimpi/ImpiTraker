using System.Text;
using System.Globalization;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Cantrack;

/// <summary>
/// Parser minimo de Cantrack para login V0, heartbeat HTBT, paquetes de tracking serie V y command ACKs.
/// Handles V4 structured ACK packets and plaintext ACK responses (e.g. "stop engine succeed").
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

        // Plaintext ACK responses from Cantrack devices do not use the *HQ,...,# envelope.
        // Examples: "stop engine succeed", "resume engine succeed".
        // We recognise any non-empty text that does NOT start with '*' as a text ACK.
        // The device sends these after executing a command — treat as CommandAck.
        if (!text.StartsWith('*'))
        {
            message = new ParsedMessage(
                Protocol,
                MessageType.CommandAck,
                null,   // IMEI not available in plaintext responses
                frame.Payload,
                text,
                frame.ReceivedAtUtc,
                ResponseCode: "TEXT",
                CorrelationKey: NormalizeTextResponseKey(text));
            return true;
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

        // V4 packet: *HQ,IMEI,V4,CMD,hhmmss,<position-fields...>#
        // CMD is the echoed command keyword (correlation key).
        // hhmmss is the timestamp from the server-sent command (correlation timestamp).
        // Position data may also be present (fields[5+]).
        if (command == "V4")
        {
            string? responseCode = parts.Length >= 4 ? parts[3] : null;
            string? correlationTimestamp = parts.Length >= 5 ? parts[4] : null;

            // Attempt to parse position data if enough fields are present.
            DateTimeOffset? gpsTimeUtc = null;
            double? latitude = null;
            double? longitude = null;
            double? speedKmh = null;
            int? headingDeg = null;
            bool isTelemetryUsable = false;
            string? telemetryError = null;

            // V4 position layout (after CMD and hhmmss) mirrors V1:
            // parts[5]=HHMMSS, parts[6]=S(status), parts[7]=lat, parts[8]=N/S,
            // parts[9]=lon, parts[10]=E/W, parts[11]=speed, parts[12]=direction, parts[13]=DDMMYY
            // We build a synthetic parts array with the same layout as V1 for reuse.
            if (parts.Length >= 14)
            {
                // Reconstruct a V1-shaped array: [0]=*HQ, [1]=IMEI, [2]=V1, [3]=YYMMDD, [4]=HHMMSS, [5..11]=position
                // V4:  parts[3]=CMD, parts[4]=hhmmss, parts[5]=HHMMSS, parts[6]=S, parts[7]=lat, parts[8]=N/S,
                //      parts[9]=lon, parts[10]=E/W, parts[11]=speed, parts[12]=dir, parts[13]=DDMMYY
                // Cantrack date in V4 is DDMMYY (field[13]); V1 is YYMMDD (field[3]).
                // Re-arrange to YYMMDD for TryParseTrackingTelemetry compatibility.
                string ddmmyy = parts[13];
                string yymmdd = ddmmyy.Length == 6
                    ? string.Concat(ddmmyy.AsSpan(4, 2), ddmmyy.AsSpan(2, 2), ddmmyy.AsSpan(0, 2))
                    : ddmmyy;

                string[] v1Parts = [
                    parts[0],       // [0] *HQ
                    parts[1],       // [1] IMEI
                    "V1",           // [2] synthetic
                    yymmdd,         // [3] YYMMDD (date)
                    parts[5],       // [4] HHMMSS (time)
                    parts[6],       // [5] validity (S field)
                    parts[7],       // [6] latitude
                    parts[8],       // [7] N/S
                    parts[9],       // [8] longitude
                    parts[10],      // [9] E/W
                    parts[11],      // [10] speed
                    parts[12],      // [11] direction
                ];
                isTelemetryUsable = TryParseTrackingTelemetry(
                    v1Parts,
                    out gpsTimeUtc,
                    out latitude,
                    out longitude,
                    out speedKmh,
                    out headingDeg,
                    out telemetryError);
            }

            message = new ParsedMessage(
                Protocol,
                MessageType.CommandAck,
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
                telemetryError,
                ResponseCode: responseCode,
                CorrelationKey: responseCode,
                CorrelationTimestamp: correlationTimestamp);

            return true;
        }

        MessageType type = command switch
        {
            "V0" => MessageType.Login,
            "HTBT" => MessageType.Heartbeat,
            "V1" => MessageType.Tracking,
            _ when command.StartsWith("V", StringComparison.Ordinal) => MessageType.Tracking,
            _ => MessageType.Unknown
        };

        DateTimeOffset? gpsTimeUtcStd = null;
        double? latitudeStd = null;
        double? longitudeStd = null;
        double? speedKmhStd = null;
        int? headingDegStd = null;
        bool isTelemetryUsableStd = true;
        string? telemetryErrorStd = null;
        if (type == MessageType.Tracking)
        {
            isTelemetryUsableStd = TryParseTrackingTelemetry(
                parts,
                out gpsTimeUtcStd,
                out latitudeStd,
                out longitudeStd,
                out speedKmhStd,
                out headingDegStd,
                out telemetryErrorStd);
        }

        message = new ParsedMessage(
            Protocol,
            type,
            string.IsNullOrWhiteSpace(imei) ? null : imei,
            frame.Payload,
            text,
            frame.ReceivedAtUtc,
            gpsTimeUtcStd,
            latitudeStd,
            longitudeStd,
            speedKmhStd,
            headingDegStd,
            isTelemetryUsableStd,
            telemetryErrorStd);

        return true;
    }

    /// <summary>
    /// Normalizes a plaintext ACK response to a stable correlation key.
    /// Example: "stop engine succeed" → "stop", "resume engine succeed" → "resume".
    /// Falls back to the full text (lowercased, trimmed) for unknown responses.
    /// </summary>
    private static string? NormalizeTextResponseKey(string text)
    {
        string lower = text.ToLowerInvariant().Trim();
        return lower switch
        {
            var s when s.StartsWith("stop", StringComparison.Ordinal) => "stop",
            var s when s.StartsWith("resume", StringComparison.Ordinal) => "resume",
            _ => lower
        };
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

        // GpsTimeUtc: Cantrack field[3] = YYMMDD (UTC date from GPS clock),
        // field[4] = HHMMSS.sss (UTC time from GPS clock).
        // Both fields are already UTC — no timezone offset or day-rollover adjustment needed
        // (unlike Coban which embeds local time in its timestamp field).
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

        // B.5: Cantrack packet format (V1/V series) fields [0..11] do not include a confirmed ACC bit.
        // Known field indices: [0]=*VT, [1]=IMEI, [2]=V1, [3]=YYMMDD, [4]=HHMMSS.sss,
        // [5]=validity(A/V), [6]=lat, [7]=N/S, [8]=lon, [9]=E/W, [10]=speed, [11]=heading.
        // Fields [12+] exist in some Cantrack variants but their meaning has not been confirmed
        // against real hardware packets. IgnitionOn, PowerConnected, and DoorOpen are left null
        // until a real Cantrack packet with ACC/PWR/Door data is captured and field indices confirmed.

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
