using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Solicitud para revocar un refresh token activo.
/// </summary>
public sealed class RevokeRequest
{
    /// <summary>
    /// Refresh token a revocar.
    /// </summary>
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
