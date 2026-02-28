using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Sessions;

/// <summary>
/// Estado mutable en memoria para una sesion TCP activa.
/// </summary>
public sealed class SessionState
{
    /// <summary>
    /// Identificador de correlacion de sesion.
    /// </summary>
    public required SessionId SessionId { get; init; }

    /// <summary>
    /// Direccion IP remota del cliente.
    /// </summary>
    public required string RemoteIp { get; init; }

    /// <summary>
    /// Puerto del listener asociado a la sesion.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Marca de tiempo UTC cuando se abrio la sesion.
    /// </summary>
    public required DateTimeOffset ConnectedAtUtc { get; init; }

    /// <summary>
    /// Marca de tiempo UTC del frame mas reciente observado.
    /// </summary>
    public DateTimeOffset LastSeenAtUtc { get; set; }

    /// <summary>
    /// IMEI parseado asociado a la sesion cuando se conoce.
    /// </summary>
    public string? Imei { get; set; }
}
