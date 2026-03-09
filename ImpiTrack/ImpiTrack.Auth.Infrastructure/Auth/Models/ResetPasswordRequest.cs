using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Solicitud para confirmar restablecimiento de contrasena.
/// </summary>
public sealed class ResetPasswordRequest
{
    /// <summary>
    /// Correo de la cuenta que sera restablecida.
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Token emitido por Identity para reset de contrasena.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Nueva contrasena en texto plano.
    /// </summary>
    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
}
