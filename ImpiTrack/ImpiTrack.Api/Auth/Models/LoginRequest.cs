using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Api.Auth.Models;

/// <summary>
/// Solicitud de inicio de sesión con credenciales de usuario.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// Nombre de usuario o correo del usuario.
    /// </summary>
    [Required]
    [MinLength(3)]
    public string UserNameOrEmail { get; set; } = string.Empty;

    /// <summary>
    /// Contraseña del usuario.
    /// </summary>
    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;
}
