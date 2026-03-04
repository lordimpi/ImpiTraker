using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Configuration;

/// <summary>
/// Opciones de configuracion para envio de correos transaccionales.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>
    /// Nombre de la seccion de configuracion.
    /// </summary>
    public const string SectionName = "Email";

    /// <summary>
    /// Indica si el envio real de correos esta habilitado.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Nombre visible del remitente.
    /// </summary>
    [MinLength(2)]
    public string FromName { get; set; } = "ImpiTrack";

    /// <summary>
    /// Correo remitente.
    /// </summary>
    [EmailAddress]
    public string FromEmail { get; set; } = "no-reply@imptrack.local";

    /// <summary>
    /// URL base para confirmacion de correo desde el enlace enviado.
    /// </summary>
    [Url]
    public string VerifyEmailBaseUrl { get; set; } = "https://localhost:5001/api/auth/verify-email/confirm";

    /// <summary>
    /// Configuracion de proveedor SMTP.
    /// </summary>
    public SmtpEmailOptions Smtp { get; set; } = new();
}

/// <summary>
/// Parametros SMTP para autenticacion y transporte de correo.
/// </summary>
public sealed class SmtpEmailOptions
{
    /// <summary>
    /// Host SMTP.
    /// </summary>
    [MinLength(1)]
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Puerto SMTP.
    /// </summary>
    [Range(1, 65_535)]
    public int Port { get; set; } = 25;

    /// <summary>
    /// Indica si se usa TLS/SSL al conectar.
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Usuario SMTP para autenticacion.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Clave SMTP para autenticacion.
    /// </summary>
    public string? Password { get; set; }
}
