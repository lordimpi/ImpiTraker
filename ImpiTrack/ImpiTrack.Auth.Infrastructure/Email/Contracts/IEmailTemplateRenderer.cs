using ImpiTrack.Auth.Infrastructure.Email.Models;

namespace ImpiTrack.Auth.Infrastructure.Email.Contracts;

/// <summary>
/// Contrato para construir plantillas de correo.
/// </summary>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Construye el correo de confirmacion de cuenta.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="email">Correo destino.</param>
    /// <param name="userName">Nombre de usuario.</param>
    /// <param name="token">Token de confirmacion de Identity.</param>
    /// <returns>Mensaje listo para encolar.</returns>
    EmailMessage BuildVerifyEmailMessage(Guid userId, string email, string userName, string token);

    /// <summary>
    /// Construye el correo de recuperacion de contrasena.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="email">Correo destino.</param>
    /// <param name="userName">Nombre de usuario.</param>
    /// <param name="token">Token de reset emitido por Identity.</param>
    /// <returns>Mensaje listo para encolar.</returns>
    EmailMessage BuildResetPasswordMessage(Guid userId, string email, string userName, string token);
}

