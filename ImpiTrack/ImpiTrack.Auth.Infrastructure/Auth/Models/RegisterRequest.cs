using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Solicitud para registrar una nueva cuenta de usuario.
/// </summary>
public sealed class RegisterRequest
{
    /// <summary>
    /// Nombre de usuario único para autenticación.
    /// </summary>
    [Required]
    [MinLength(3)]
    [MaxLength(64)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Correo electrónico principal de la cuenta.
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Contraseña inicial del usuario.
    /// </summary>
    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Nombre visible opcional del usuario.
    /// </summary>
    [MaxLength(120)]
    public string? FullName { get; set; }
}
