using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Shared.Models;

/// <summary>
/// Solicitud para vincular un dispositivo GPS por IMEI.
/// </summary>
public sealed class BindDeviceRequest
{
    /// <summary>
    /// IMEI del dispositivo a vincular.
    /// </summary>
    [Required]
    [MinLength(8)]
    [MaxLength(32)]
    public string Imei { get; set; } = string.Empty;
}
