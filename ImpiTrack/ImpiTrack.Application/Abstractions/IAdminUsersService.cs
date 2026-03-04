using ImpiTrack.DataAccess.Abstractions;

namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Casos de uso administrativos para usuarios y planes.
/// </summary>
public interface IAdminUsersService
{
    /// <summary>
    /// Lista usuarios con estado de plan y cuota.
    /// </summary>
    /// <param name="limit">Maximo de filas.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Usuarios encontrados.</returns>
    Task<IReadOnlyList<UserAccountOverview>> GetUsersAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene resumen de un usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resumen o <c>null</c> si el usuario no existe.</returns>
    Task<UserAccountSummary?> GetUserSummaryAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Asigna plan activo a un usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="planCode">Codigo de plan.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado de la operacion.</returns>
    Task<SetUserPlanResult> SetUserPlanAsync(Guid userId, string planCode, CancellationToken cancellationToken);

    /// <summary>
    /// Vincula un IMEI para un usuario especifico.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="imei">IMEI a vincular.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado de vinculacion.</returns>
    Task<AdminBindDeviceResult> BindDeviceAsync(Guid userId, string imei, CancellationToken cancellationToken);

    /// <summary>
    /// Desvincula un IMEI para un usuario especifico.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="imei">IMEI a desvincular.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Estado de desvinculacion.</returns>
    Task<UnbindDeviceStatus> UnbindDeviceAsync(Guid userId, string imei, CancellationToken cancellationToken);
}
