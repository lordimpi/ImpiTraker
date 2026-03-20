namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Resuelve los identificadores de usuarios propietarios de un IMEI para enrutamiento de notificaciones.
/// </summary>
public interface IDeviceOwnershipResolver
{
    /// <summary>
    /// Obtiene los identificadores de usuarios con vinculo activo para el IMEI indicado.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo a resolver.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Lista de identificadores de usuario propietarios. Vacia si no hay vinculos activos.</returns>
    Task<IReadOnlyList<Guid>> GetUserIdsForImeiAsync(string imei, CancellationToken cancellationToken);
}
