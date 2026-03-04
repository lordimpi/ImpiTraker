namespace ImpiTrack.Tcp.Core.EventBus;

/// <summary>
/// Mensaje publicado en el bus de eventos interno.
/// </summary>
/// <param name="Topic">Topic destino.</param>
/// <param name="Payload">Payload publicado.</param>
/// <param name="PublishedAtUtc">Fecha UTC de publicacion.</param>
public sealed record EventBusMessage(
    string Topic,
    object? Payload,
    DateTimeOffset PublishedAtUtc);
