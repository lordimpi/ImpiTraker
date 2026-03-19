namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Operaciones de cuenta de usuario, plan y vinculacion de GPS.
/// </summary>
public interface IUserAccountRepository
{
    /// <summary>
    /// Crea perfil y plan por defecto para un usuario cuando no existen.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="email">Correo principal del usuario.</param>
    /// <param name="fullName">Nombre visible opcional.</param>
    /// <param name="nowUtc">Fecha UTC de provisionamiento.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task EnsureUserProvisioningAsync(
        Guid userId,
        string email,
        string? fullName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene resumen de cuenta para el usuario indicado.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resumen de cuenta o <c>null</c> si no existe provisionamiento.</returns>
    Task<UserAccountSummary?> GetUserSummaryAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene los GPS vinculados a un usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Lista de vinculos activos.</returns>
    Task<IReadOnlyList<UserDeviceBinding>> GetUserDevicesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Vincula un IMEI a la cuenta del usuario validando cuota y ownership.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="imei">IMEI a vincular.</param>
    /// <param name="nowUtc">Fecha UTC de operacion.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Estado de la vinculacion.</returns>
    Task<BindDeviceResult> BindDeviceAsync(
        Guid userId,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Desvincula un IMEI de la cuenta del usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="imei">IMEI a desvincular.</param>
    /// <param name="nowUtc">Fecha UTC de operacion.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si se desactivo el vinculo.</returns>
    Task<bool> UnbindDeviceAsync(
        Guid userId,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lista usuarios para operacion administrativa.
    /// </summary>
    /// <param name="query">Consulta paginada con filtros y ordenamiento.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Vista resumida de usuarios paginada.</returns>
    Task<PagedResult<UserAccountOverview>> GetUsersAsync(AdminUserListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Lista planes administrables para UI de administracion.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Planes activos disponibles.</returns>
    Task<IReadOnlyList<AdminPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asigna un plan activo a un usuario finalizando el anterior.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="planCode">Codigo de plan a activar.</param>
    /// <param name="nowUtc">Fecha UTC de operacion.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si se aplico correctamente.</returns>
    Task<bool> SetUserPlanAsync(
        Guid userId,
        string planCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Actualiza el alias de un dispositivo vinculado al usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="alias">Nuevo alias o null para borrar.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si se actualizo el vinculo.</returns>
    Task<bool> UpdateDeviceAliasAsync(
        Guid userId,
        string imei,
        string? alias,
        CancellationToken cancellationToken);
}
