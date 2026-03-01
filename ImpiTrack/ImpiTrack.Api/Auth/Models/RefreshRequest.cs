using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Api.Auth.Models;

/// <summary>
/// Solicitud para renovar sesión usando refresh token.
/// </summary>
public sealed class RefreshRequest
{
    /// <summary>
    /// Refresh token emitido previamente.
    /// </summary>
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
