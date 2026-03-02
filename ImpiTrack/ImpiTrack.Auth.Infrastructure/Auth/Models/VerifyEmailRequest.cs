using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Solicitud para confirmar correo electrónico de una cuenta.
/// </summary>
public sealed class VerifyEmailRequest
{
    /// <summary>
    /// Identificador del usuario a confirmar.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Token de confirmación emitido por Identity.
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;
}
