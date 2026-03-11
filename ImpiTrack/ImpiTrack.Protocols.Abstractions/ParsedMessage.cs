namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Representacion normalizada de un mensaje de protocolo parseado.
/// </summary>
/// <param name="Protocol">Familia de protocolo resuelta.</param>
/// <param name="MessageType">Categoria de mensaje normalizada.</param>
/// <param name="Imei">Identificador del dispositivo parseado cuando esta disponible.</param>
/// <param name="RawPayload">Payload original del frame usado por el parser.</param>
/// <param name="Text">Representacion de texto del payload crudo.</param>
/// <param name="ReceivedAtUtc">Marca de tiempo UTC de recepcion del frame.</param>
/// <param name="GpsTimeUtc">Marca de tiempo GPS UTC cuando el protocolo la provee.</param>
/// <param name="Latitude">Latitud en grados decimales cuando aplica.</param>
/// <param name="Longitude">Longitud en grados decimales cuando aplica.</param>
/// <param name="SpeedKmh">Velocidad en km/h cuando aplica.</param>
/// <param name="HeadingDeg">Rumbo en grados cuando aplica.</param>
/// <param name="IsTelemetryUsable">Indica si el mensaje puede derivarse de forma util a telemetria funcional.</param>
/// <param name="TelemetryError">Codigo corto de error cuando un tracking no es util para telemetria.</param>
public sealed record ParsedMessage(
    ProtocolId Protocol,
    MessageType MessageType,
    string? Imei,
    ReadOnlyMemory<byte> RawPayload,
    string Text,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? GpsTimeUtc = null,
    double? Latitude = null,
    double? Longitude = null,
    double? SpeedKmh = null,
    int? HeadingDeg = null,
    bool IsTelemetryUsable = true,
    string? TelemetryError = null);
