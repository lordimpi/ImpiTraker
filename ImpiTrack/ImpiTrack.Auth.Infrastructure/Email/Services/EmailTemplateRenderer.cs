using ImpiTrack.Auth.Infrastructure.Configuration;
using ImpiTrack.Auth.Infrastructure.Email.Contracts;
using ImpiTrack.Auth.Infrastructure.Email.Models;
using ImpiTrack.DataAccess.IOptionPattern;
using System.Net;

namespace ImpiTrack.Auth.Infrastructure.Email.Services;

/// <summary>
/// Constructor de plantillas de correo para flujos de autenticacion.
/// </summary>
public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly IGenericOptionsService<EmailOptions> _emailOptionsService;

    /// <summary>
    /// Crea un renderer basado en opciones de correo.
    /// </summary>
    /// <param name="emailOptionsService">Opciones de templates y URLs.</param>
    public EmailTemplateRenderer(IGenericOptionsService<EmailOptions> emailOptionsService)
    {
        _emailOptionsService = emailOptionsService;
    }

    /// <inheritdoc />
    public EmailMessage BuildVerifyEmailMessage(Guid userId, string email, string userName, string token)
    {
        EmailOptions options = _emailOptionsService.GetSnapshotOptions();
        string encodedToken = Uri.EscapeDataString(token);
        string confirmUrl = BuildVerifyEmailUrl(options.VerifyEmailBaseUrl, userId, encodedToken);
        string safeUserName = WebUtility.HtmlEncode(userName);
        string safeConfirmUrl = WebUtility.HtmlEncode(confirmUrl);

        string subject = "Confirma tu correo de ImpiTrack";
        string htmlBody = $"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1" />
              <title>Confirma tu correo</title>
            </head>
            <body style="margin:0;padding:24px;background:#f4f7fb;font-family:Segoe UI,Arial,sans-serif;color:#102a43;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #d9e2ec;border-radius:12px;">
                <tr>
                  <td style="padding:24px;">
                    <h1 style="margin:0 0 12px;font-size:22px;line-height:1.3;">Confirma tu correo</h1>
                    <p style="margin:0 0 12px;font-size:15px;line-height:1.6;">Hola <strong>{safeUserName}</strong>,</p>
                    <p style="margin:0 0 20px;font-size:15px;line-height:1.6;">
                      Para activar tu cuenta en ImpiTrack, confirma tu correo con el siguiente boton:
                    </p>
                    <p style="margin:0 0 20px;">
                      <a href="{safeConfirmUrl}" style="display:inline-block;background:#0b6efd;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;">
                        Confirmar correo
                      </a>
                    </p>
                    <p style="margin:0 0 8px;font-size:13px;color:#486581;">Si el boton no funciona, copia y pega este enlace en tu navegador:</p>
                    <p style="margin:0 0 18px;word-break:break-all;font-size:13px;color:#102a43;">
                      <a href="{safeConfirmUrl}" style="color:#0b6efd;text-decoration:underline;">{safeConfirmUrl}</a>
                    </p>
                    <p style="margin:0;font-size:13px;color:#486581;">Si no solicitaste esta cuenta, ignora este correo.</p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        string textBody =
            $"Hola {userName}," + Environment.NewLine +
            "Para activar tu cuenta, confirma tu correo con este enlace:" + Environment.NewLine +
            confirmUrl + Environment.NewLine +
            "Si no solicitaste esta cuenta, ignora este mensaje.";

        return new EmailMessage(
            email,
            subject,
            htmlBody,
            textBody,
            TemplateName: "verify_email",
            UserId: userId);
    }

    /// <inheritdoc />
    public EmailMessage BuildResetPasswordMessage(Guid userId, string email, string userName, string token)
    {
        EmailOptions options = _emailOptionsService.GetSnapshotOptions();
        string encodedToken = Uri.EscapeDataString(token);
        string encodedEmail = Uri.EscapeDataString(email);
        string resetUrl = BuildResetPasswordUrl(options.ResetPasswordBaseUrl, encodedEmail, encodedToken);
        string safeUserName = WebUtility.HtmlEncode(userName);
        string safeResetUrl = WebUtility.HtmlEncode(resetUrl);

        string subject = "Restablece tu contrasena de ImpiTrack";
        string htmlBody = $"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1" />
              <title>Restablece tu contrasena</title>
            </head>
            <body style="margin:0;padding:24px;background:#f4f7fb;font-family:Segoe UI,Arial,sans-serif;color:#102a43;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #d9e2ec;border-radius:12px;">
                <tr>
                  <td style="padding:24px;">
                    <h1 style="margin:0 0 12px;font-size:22px;line-height:1.3;">Restablece tu contrasena</h1>
                    <p style="margin:0 0 12px;font-size:15px;line-height:1.6;">Hola <strong>{safeUserName}</strong>,</p>
                    <p style="margin:0 0 20px;font-size:15px;line-height:1.6;">
                      Recibimos una solicitud para cambiar tu contrasena en ImpiTrack. Usa el siguiente boton para continuar:
                    </p>
                    <p style="margin:0 0 20px;">
                      <a href="{safeResetUrl}" style="display:inline-block;background:#0b6efd;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;">
                        Restablecer contrasena
                      </a>
                    </p>
                    <p style="margin:0 0 8px;font-size:13px;color:#486581;">Si el boton no funciona, copia y pega este enlace en tu navegador:</p>
                    <p style="margin:0 0 18px;word-break:break-all;font-size:13px;color:#102a43;">
                      <a href="{safeResetUrl}" style="color:#0b6efd;text-decoration:underline;">{safeResetUrl}</a>
                    </p>
                    <p style="margin:0;font-size:13px;color:#486581;">Si no solicitaste este cambio, ignora este correo.</p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        string textBody =
            $"Hola {userName}," + Environment.NewLine +
            "Recibimos una solicitud para cambiar tu contrasena. Usa este enlace para continuar:" + Environment.NewLine +
            resetUrl + Environment.NewLine +
            "Si no solicitaste este cambio, ignora este mensaje.";

        return new EmailMessage(
            email,
            subject,
            htmlBody,
            textBody,
            TemplateName: "reset_password",
            UserId: userId);
    }

    private static string BuildVerifyEmailUrl(string baseUrl, Guid userId, string encodedToken)
    {
        string separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}userId={userId:D}&token={encodedToken}";
    }

    private static string BuildResetPasswordUrl(string baseUrl, string encodedEmail, string encodedToken)
    {
        string separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}email={encodedEmail}&token={encodedToken}";
    }
}

