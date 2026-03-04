using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Shared.Models;

/// <summary>
/// Solicitud administrativa para asignar plan activo a un usuario.
/// </summary>
public sealed class SetUserPlanRequest
{
    /// <summary>
    /// Código del plan a activar (por ejemplo: BASIC, PRO, ENTERPRISE).
    /// </summary>
    [Required]
    [MinLength(3)]
    [MaxLength(32)]
    public string PlanCode { get; set; } = string.Empty;
}
