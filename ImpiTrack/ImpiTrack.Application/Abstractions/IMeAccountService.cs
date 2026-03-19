namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Casos de uso para autogestion de cuenta del usuario autenticado.
/// </summary>
public interface IMeAccountService
{
    /// <summary>
    /// Obtiene el resumen de la cuenta.
    /// </summary>
    /// <param name="userId">Identificador del usuario autenticado.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resumen de cuenta o <c>null</c> si el usuario no existe.</returns>
    Task<UserAccountSummary?> GetSummaryAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene dispositivos vinculados.
    /// </summary>
    /// <param name="userId">Identificador del usuario autenticado.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Lista de dispositivos o <c>null</c> si el usuario no existe.</returns>
    Task<IReadOnlyList<UserDeviceBinding>?> GetDevicesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Vincula un IMEI a la cuenta del usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario autenticado.</param>
    /// <param name="imei">IMEI a vincular.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado de vinculacion o <c>null</c> si el usuario no existe.</returns>
    Task<BindDeviceResult?> BindDeviceAsync(Guid userId, string imei, CancellationToken cancellationToken);

    /// <summary>
    /// Desvincula un IMEI de la cuenta del usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario autenticado.</param>
    /// <param name="imei">IMEI a desvincular.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Estado de la operacion.</returns>
    Task<UnbindDeviceStatus> UnbindDeviceAsync(Guid userId, string imei, CancellationToken cancellationToken);

    /// <summary>
    /// Actualiza el alias de un dispositivo vinculado al usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario autenticado.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="alias">Nuevo alias o null para borrar.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Estado de la operacion.</returns>
    Task<UpdateDeviceAliasStatus> UpdateDeviceAliasAsync(Guid userId, string imei, string? alias, CancellationToken cancellationToken);
}
