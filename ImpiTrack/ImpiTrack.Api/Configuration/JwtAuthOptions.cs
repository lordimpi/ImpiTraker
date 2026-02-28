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
    public string Issuer { get; set; } = "ImpiTrack";

    /// <summary>
    /// Audiencia esperada del token.
    /// </summary>
    public string Audience { get; set; } = "ImpiTrack.Api";

    /// <summary>
    /// Clave simetrica para firma HMAC de tokens.
    /// </summary>
    public string SigningKey { get; set; } = "imptrack-dev-signing-key-change-me-2026";
}
