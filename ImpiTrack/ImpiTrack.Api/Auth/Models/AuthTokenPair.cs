namespace ImpiTrack.Api.Auth.Models;

/// <summary>
/// Resultado de autenticación con access token y refresh token.
/// </summary>
/// <param name="AccessToken">JWT para autorización de API.</param>
/// <param name="AccessTokenExpiresAtUtc">Fecha de expiración UTC del JWT.</param>
/// <param name="RefreshToken">Token de refresco para renovación de sesión.</param>
/// <param name="RefreshTokenExpiresAtUtc">Fecha de expiración UTC del refresh token.</param>
public sealed record AuthTokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);
