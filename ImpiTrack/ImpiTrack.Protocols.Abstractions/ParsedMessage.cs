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
public sealed record ParsedMessage(
    ProtocolId Protocol,
    MessageType MessageType,
    string? Imei,
    ReadOnlyMemory<byte> RawPayload,
    string Text,
    DateTimeOffset ReceivedAtUtc);
