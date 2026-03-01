using ImpiTrack.Api.Auth.Models;

namespace ImpiTrack.Api.Auth.Contracts;

/// <summary>
/// Servicio para emitir, refrescar y revocar tokens de autenticación.
/// </summary>
public interface IAuthTokenService
{
    /// <summary>
    /// Emite un par access token y refresh token para un usuario autenticado.
    /// </summary>
    /// <param name="userNameOrEmail">Nombre de usuario o correo del usuario.</param>
    /// <param name="password">Contraseña en texto plano.</param>
    /// <param name="remoteIp">IP remota del cliente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resultado de autenticación o <c>null</c> cuando credenciales son inválidas.</returns>
    Task<AuthTokenPair?> LoginAsync(
        string userNameOrEmail,
        string password,
        string remoteIp,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rota un refresh token activo y genera un nuevo par de tokens.
    /// </summary>
    /// <param name="refreshToken">Refresh token en texto plano.</param>
    /// <param name="remoteIp">IP remota del cliente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Nuevo par de tokens o <c>null</c> cuando el refresh token no es válido.</returns>
    Task<AuthTokenPair?> RefreshAsync(string refreshToken, string remoteIp, CancellationToken cancellationToken);

    /// <summary>
    /// Revoca un refresh token activo.
    /// </summary>
    /// <param name="refreshToken">Refresh token en texto plano.</param>
    /// <param name="remoteIp">IP remota del cliente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si se revocó el token; de lo contrario <c>false</c>.</returns>
    Task<bool> RevokeAsync(string refreshToken, string remoteIp, CancellationToken cancellationToken);
}
