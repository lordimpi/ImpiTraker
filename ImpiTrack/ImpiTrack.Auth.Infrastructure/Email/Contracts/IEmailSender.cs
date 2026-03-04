using ImpiTrack.Auth.Infrastructure.Email.Models;

namespace ImpiTrack.Auth.Infrastructure.Email.Contracts;

/// <summary>
/// Contrato para envio de correos transaccionales.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Envia un mensaje de correo al proveedor configurado.
    /// </summary>
    /// <param name="message">Mensaje a enviar.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>
    /// <c>true</c> cuando el envio se completo o el modo real esta deshabilitado;
    /// en otro caso <c>false</c>.
    /// </returns>
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
