namespace ImpiTrack.DataAccess.Abstractions;

/// <summary>
/// Resumen de cuenta del usuario autenticado.
/// </summary>
/// <param name="UserId">Identificador del usuario.</param>
/// <param name="Email">Correo principal de la cuenta.</param>
/// <param name="FullName">Nombre visible del usuario.</param>
/// <param name="PlanCode">Codigo del plan activo.</param>
/// <param name="PlanName">Nombre del plan activo.</param>
/// <param name="MaxGps">Cantidad maxima de GPS permitida por plan.</param>
/// <param name="UsedGps">Cantidad actual de GPS vinculados.</param>
public sealed record UserAccountSummary(
    Guid UserId,
    string Email,
    string? FullName,
    string PlanCode,
    string PlanName,
    int MaxGps,
    int UsedGps);

/// <summary>
/// Vinculo de un GPS con un usuario.
/// </summary>
/// <param name="DeviceId">Identificador interno del dispositivo.</param>
/// <param name="Imei">IMEI del dispositivo.</param>
/// <param name="BoundAtUtc">Fecha UTC de vinculacion.</param>
public sealed record UserDeviceBinding(
    Guid DeviceId,
    string Imei,
    DateTimeOffset BoundAtUtc);

/// <summary>
/// Vista administrativa de usuario para listado.
/// </summary>
/// <param name="UserId">Identificador del usuario.</param>
/// <param name="Email">Correo principal.</param>
/// <param name="FullName">Nombre visible.</param>
/// <param name="PlanCode">Codigo de plan activo.</param>
/// <param name="MaxGps">Cuota de GPS del plan activo.</param>
/// <param name="UsedGps">GPS activos vinculados.</param>
public sealed record UserAccountOverview(
    Guid UserId,
    string Email,
    string? FullName,
    string PlanCode,
    int MaxGps,
    int UsedGps);

/// <summary>
/// Resultado de operacion de vinculacion de dispositivo.
/// </summary>
public enum BindDeviceStatus
{
    /// <summary>
    /// El dispositivo quedo vinculado al usuario.
    /// </summary>
    Bound = 1,

    /// <summary>
    /// El dispositivo ya estaba vinculado al mismo usuario.
    /// </summary>
    AlreadyBound = 2,

    /// <summary>
    /// El IMEI ya pertenece a otro usuario activo.
    /// </summary>
    OwnedByAnotherUser = 3,

    /// <summary>
    /// La cuenta no tiene cuota disponible para mas GPS.
    /// </summary>
    QuotaExceeded = 4,

    /// <summary>
    /// El usuario no tiene suscripcion activa para operar.
    /// </summary>
    MissingActivePlan = 5
}

/// <summary>
/// Resultado de vinculacion de IMEI con metadata minima.
/// </summary>
/// <param name="Status">Estado de la operacion.</param>
/// <param name="DeviceId">Id del dispositivo cuando aplica.</param>
public sealed record BindDeviceResult(
    BindDeviceStatus Status,
    Guid? DeviceId);

