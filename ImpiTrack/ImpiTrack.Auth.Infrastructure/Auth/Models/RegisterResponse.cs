namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Resultado exitoso del registro de una cuenta.
/// </summary>
/// <param name="UserId">Identificador del usuario creado.</param>
/// <param name="UserName">Nombre de usuario registrado.</param>
/// <param name="Email">Correo registrado.</param>
/// <param name="RequiresEmailVerification">Indica si debe verificar correo antes de iniciar sesion.</param>
/// <param name="EmailVerificationToken">
/// Token de verificacion de correo para flujo API.
/// En produccion puede ocultarse por politicas de seguridad.
/// </param>
public sealed record RegisterResponse(
    Guid UserId,
    string UserName,
    string Email,
    bool RequiresEmailVerification,
    string EmailVerificationToken);
