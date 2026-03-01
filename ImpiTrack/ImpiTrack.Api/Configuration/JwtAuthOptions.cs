using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Api.Configuration;

/// <summary>
/// Opciones de autenticacion JWT para la Web API.
/// </summary>
public sealed class JwtAuthOptions
{
    /// <summary>
    /// Nombre de la seccion de configuracion en appsettings.
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Emisor esperado del token.
    /// </summary>
    [Required]
    [MinLength(3)]
    public string Issuer { get; set; } = "ImpiTrack";

    /// <summary>
    /// Audiencia esperada del token.
    /// </summary>
    [Required]
    [MinLength(3)]
    public string Audience { get; set; } = "ImpiTrack.Api";

    /// <summary>
    /// Clave simetrica para firma HMAC de tokens.
    /// </summary>
    [Required]
    [MinLength(16)]
    public string SigningKey { get; set; } = "imptrack-dev-signing-key-change-me-2026";

    /// <summary>
    /// Minutos de vigencia del access token JWT.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>
    /// Días de vigencia del refresh token.
    /// </summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;
}
