using ImpiTrack.Auth.Infrastructure.Auth.Models;

namespace ImpiTrack.Auth.Infrastructure.Auth.Contracts;

/// <summary>
/// Servicio para emitir, refrescar y revocar tokens de autenticación.
/// </summary>
public interface IAuthTokenService
{
    /// <summary>
    /// Registra un nuevo usuario final y devuelve token de verificación de correo.
    /// </summary>
    /// <param name="userName">Nombre de usuario.</param>
    /// <param name="email">Correo de la cuenta.</param>
    /// <param name="password">Contraseña inicial.</param>
    /// <param name="fullName">Nombre visible opcional.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resultado de registro con estado y errores.</returns>
    Task<RegisterResult> RegisterAsync(
        string userName,
        string email,
        string password,
        string? fullName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Confirma el correo de un usuario usando token de Identity.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="token">Token de confirmación emitido por Identity.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resultado de verificación.</returns>
    Task<VerifyEmailResult> VerifyEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken);

    /// <summary>
    /// Solicita un restablecimiento de contrasena para la cuenta asociada al correo.
    /// </summary>
    /// <param name="email">Correo de la cuenta.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Tarea completada sin revelar si la cuenta existe.</returns>
    Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken);

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
    /// Restablece la contrasena de una cuenta usando un token emitido por Identity.
    /// </summary>
    /// <param name="email">Correo de la cuenta.</param>
    /// <param name="token">Token de restablecimiento.</param>
    /// <param name="newPassword">Nueva contrasena en texto plano.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado del restablecimiento solicitado.</returns>
    Task<ResetPasswordResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revoca un refresh token activo.
    /// </summary>
    /// <param name="refreshToken">Refresh token en texto plano.</param>
    /// <param name="remoteIp">IP remota del cliente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si se revocó el token; de lo contrario <c>false</c>.</returns>
    Task<bool> RevokeAsync(string refreshToken, string remoteIp, CancellationToken cancellationToken);
}

