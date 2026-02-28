namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Representa un frame completo decodificado desde el flujo de bytes TCP.
/// </summary>
/// <param name="Payload">Bytes crudos del frame, incluyendo delimitador cuando aplica.</param>
/// <param name="ReceivedAtUtc">Marca de tiempo cuando el decoder materializo el frame.</param>
public sealed record Frame(
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset ReceivedAtUtc);
