namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Resultado de asignacion de plan por operacion administrativa.
/// </summary>
/// <param name="Status">Estado final de la asignacion.</param>
/// <param name="Summary">Resumen actualizado cuando aplica.</param>
public sealed record SetUserPlanResult(
    SetUserPlanStatus Status,
    UserAccountSummary? Summary);

/// <summary>
/// Estados de la operacion de asignar plan.
/// </summary>
public enum SetUserPlanStatus
{
    /// <summary>
    /// Operacion completada correctamente.
    /// </summary>
    Updated = 1,

    /// <summary>
    /// Usuario objetivo no existe.
    /// </summary>
    UserNotFound = 2,

    /// <summary>
    /// Codigo de plan invalido.
    /// </summary>
    InvalidPlanCode = 3
}

/// <summary>
/// Resultado de vinculacion administrativa de dispositivo.
/// </summary>
/// <param name="Status">Estado final del intento.</param>
/// <param name="Binding">Datos de vinculacion cuando aplica.</param>
public sealed record AdminBindDeviceResult(
    AdminBindDeviceStatus Status,
    BindDeviceResult? Binding);

/// <summary>
/// Estados de vinculacion administrativa.
/// </summary>
public enum AdminBindDeviceStatus
{
    /// <summary>
    /// Operacion completada.
    /// </summary>
    Completed = 1,

    /// <summary>
    /// Usuario objetivo no existe.
    /// </summary>
    UserNotFound = 2
}

/// <summary>
/// Estado de desvinculacion para operaciones de cuenta.
/// </summary>
public enum UnbindDeviceStatus
{
    /// <summary>
    /// Dispositivo desvinculado.
    /// </summary>
    Removed = 1,

    /// <summary>
    /// Usuario no existe.
    /// </summary>
    UserNotFound = 2,

    /// <summary>
    /// No existe un vinculo activo para el IMEI.
    /// </summary>
    BindingNotFound = 3
}

/// <summary>
/// Estado de actualizacion de alias de dispositivo.
/// </summary>
public enum UpdateDeviceAliasStatus
{
    /// <summary>
    /// Alias actualizado correctamente.
    /// </summary>
    Updated = 1,

    /// <summary>
    /// Usuario no existe.
    /// </summary>
    UserNotFound = 2,

    /// <summary>
    /// No existe un vinculo activo para el IMEI.
    /// </summary>
    BindingNotFound = 3,

    /// <summary>
    /// El alias excede la longitud maxima permitida.
    /// </summary>
    AliasTooLong = 4
}

/// <summary>
/// Resultado de operacion de alias con datos minimos.
/// </summary>
/// <param name="Imei">IMEI del dispositivo.</param>
/// <param name="Alias">Alias asignado o null si fue borrado.</param>
public sealed record DeviceAliasResult(
    string Imei,
    string? Alias);
