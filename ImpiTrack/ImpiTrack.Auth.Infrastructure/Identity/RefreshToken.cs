namespace ImpiTrack.Auth.Infrastructure.Identity;

/// <summary>
/// Token de refresco emitido para renovar access tokens JWT.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>
    /// Identificador primario del refresh token.
    /// </summary>
    public Guid RefreshTokenId { get; set; }

    /// <summary>
    /// Identificador del usuario propietario del token.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Hash SHA-256 del refresh token en claro.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Fecha de emisión del token en UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Dirección IP origen durante la emisión.
    /// </summary>
    public string CreatedByIp { get; set; } = "unknown";

    /// <summary>
    /// Fecha de expiración del token en UTC.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// Fecha de revocación cuando aplica.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }

    /// <summary>
    /// Dirección IP origen durante la revocación.
    /// </summary>
    public string? RevokedByIp { get; set; }

    /// <summary>
    /// Hash del token de reemplazo cuando hubo rotación.
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>
    /// Usuario asociado al token.
    /// </summary>
    public ApplicationUser? User { get; set; }

    /// <summary>
    /// Indica si el token sigue activo y utilizable.
    /// </summary>
    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTimeOffset.UtcNow;
}
