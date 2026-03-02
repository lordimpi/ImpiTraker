using ImpiTrack.Auth.Infrastructure.Email.Contracts;
using ImpiTrack.Auth.Infrastructure.Email.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpiTrack.Auth.Infrastructure.Email.Services;

/// <summary>
/// Worker de fondo que consume cola y envia correos.
/// </summary>
public sealed class EmailDispatchBackgroundService : BackgroundService
{
    private readonly IEmailDispatchQueue _queue;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<EmailDispatchBackgroundService> _logger;

    /// <summary>
    /// Crea el worker de despacho de correos.
    /// </summary>
    /// <param name="queue">Cola de mensajes pendiente.</param>
    /// <param name="emailSender">Proveedor de envio de correo.</param>
    /// <param name="logger">Logger estructurado.</param>
    public EmailDispatchBackgroundService(
        IEmailDispatchQueue queue,
        IEmailSender emailSender,
        ILogger<EmailDispatchBackgroundService> logger)
    {
        _queue = queue;
        _emailSender = emailSender;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (EmailMessage message in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                bool sent = await _emailSender.SendAsync(message, stoppingToken);
                if (!sent)
                {
                    _logger.LogWarning(
                        "email_send_failed template={template} to={to} userId={userId}",
                        message.TemplateName,
                        message.To,
                        message.UserId);

                    continue;
                }

                _logger.LogInformation(
                    "email_sent template={template} to={to} userId={userId}",
                    message.TemplateName,
                    message.To,
                    message.UserId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "email_send_exception template={template} to={to} userId={userId}",
                    message.TemplateName,
                    message.To,
                    message.UserId);
            }
        }
    }
}
