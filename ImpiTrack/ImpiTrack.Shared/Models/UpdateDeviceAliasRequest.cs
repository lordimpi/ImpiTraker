namespace ImpiTrack.Shared.Models;

/// <summary>
/// Solicitud para asignar o borrar el alias de un dispositivo.
/// </summary>
public sealed class UpdateDeviceAliasRequest
{
    /// <summary>
    /// Alias a asignar al dispositivo. Null o vacio para borrar.
    /// </summary>
    public string? Alias { get; set; }
}
