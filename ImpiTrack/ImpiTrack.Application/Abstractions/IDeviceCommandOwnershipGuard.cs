namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Verifica si un usuario es propietario activo de un IMEI antes de aceptar comandos.
/// Consulta la tabla <c>user_devices</c> para vinculos activos.
/// </summary>
public interface IDeviceCommandOwnershipGuard
{
    /// <summary>
    /// Indica si el usuario tiene un vinculo activo con el IMEI.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si el vinculo activo existe.</returns>
    Task<bool> IsOwnerAsync(string imei, Guid userId, CancellationToken cancellationToken);
}
