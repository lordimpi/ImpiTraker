namespace ImpiTrack.Auth.Infrastructure.Email.Models;

/// <summary>
/// Representa un mensaje de correo listo para despacho.
/// </summary>
/// <param name="To">Correo destino.</param>
/// <param name="Subject">Asunto del correo.</param>
/// <param name="HtmlBody">Contenido HTML.</param>
/// <param name="TextBody">Contenido en texto plano.</param>
/// <param name="TemplateName">Nombre logico de plantilla.</param>
/// <param name="UserId">Usuario asociado cuando aplique.</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string TemplateName,
    Guid? UserId = null);
