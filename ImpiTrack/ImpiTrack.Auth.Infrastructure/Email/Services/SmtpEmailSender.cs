using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using ImpiTrack.Auth.Infrastructure.Configuration;
using ImpiTrack.Auth.Infrastructure.Email.Contracts;
using ImpiTrack.Auth.Infrastructure.Email.Models;
using ImpiTrack.Shared.Options;
using Microsoft.Extensions.Logging;

namespace ImpiTrack.Auth.Infrastructure.Email.Services;

/// <summary>
/// Implementacion SMTP para envio de correos.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IGenericOptionsService<EmailOptions> _emailOptionsService;
    private readonly ILogger<SmtpEmailSender> _logger;

    /// <summary>
    /// Crea un sender SMTP basado en opciones tipadas.
    /// </summary>
    /// <param name="emailOptionsService">Opciones de correo.</param>
    /// <param name="logger">Logger estructurado.</param>
    public SmtpEmailSender(
        IGenericOptionsService<EmailOptions> emailOptionsService,
        ILogger<SmtpEmailSender> logger)
    {
        _emailOptionsService = emailOptionsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        EmailOptions options = _emailOptionsService.GetOptions();
        if (!options.Enabled)
        {
            _logger.LogInformation(
                "email_disabled template={template} to={to} userId={userId}",
                message.TemplateName,
                message.To,
                message.UserId);

            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var client = BuildClient(options);
        string plainBody = string.IsNullOrWhiteSpace(message.TextBody)
            ? message.Subject
            : message.TextBody;

        using var mail = new MailMessage
        {
            From = new MailAddress(options.FromEmail, options.FromName),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            Body = plainBody,
            IsBodyHtml = false
        };

        mail.To.Add(message.To);

        mail.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(
                plainBody,
                Encoding.UTF8,
                MediaTypeNames.Text.Plain));

        mail.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(
                message.HtmlBody,
                Encoding.UTF8,
                MediaTypeNames.Text.Html));

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
            return true;
        }
        catch (SmtpException exception)
        {
            _logger.LogError(
                exception,
                "smtp_send_failed host={host} port={port} to={to}",
                options.Smtp.Host,
                options.Smtp.Port,
                message.To);

            return false;
        }
    }

    private static SmtpClient BuildClient(EmailOptions options)
    {
        var client = new SmtpClient(options.Smtp.Host, options.Smtp.Port)
        {
            EnableSsl = options.Smtp.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(options.Smtp.UserName))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(options.Smtp.UserName, options.Smtp.Password);
        }
        else
        {
            client.UseDefaultCredentials = true;
        }

        return client;
    }
}
