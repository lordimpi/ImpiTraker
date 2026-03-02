using ImpiTrack.Auth.Infrastructure.Email.Models;

namespace ImpiTrack.Auth.Infrastructure.Email.Contracts;

/// <summary>
/// Cola asincrona para desacoplar solicitudes HTTP del envio de correo.
/// </summary>
public interface IEmailDispatchQueue
{
    /// <summary>
    /// Intenta encolar un mensaje para despacho posterior.
    /// </summary>
    /// <param name="message">Mensaje a encolar.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si fue encolado; de lo contrario <c>false</c>.</returns>
    ValueTask<bool> EnqueueAsync(EmailMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Lee mensajes encolados hasta cancelacion.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion del consumidor.</param>
    /// <returns>Secuencia asincrona de mensajes.</returns>
    IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken);
}
