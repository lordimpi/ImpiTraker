using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Solicitud para iniciar recuperacion de contrasena por correo.
/// </summary>
public sealed class ForgotPasswordRequest
{
    /// <summary>
    /// Correo de la cuenta que solicita el restablecimiento.
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
