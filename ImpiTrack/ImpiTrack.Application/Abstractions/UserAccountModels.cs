namespace ImpiTrack.Application.Abstractions;

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
/// Parametros de consulta para listado administrativo de usuarios.
/// </summary>
/// <param name="Page">Pagina solicitada (base 1).</param>
/// <param name="PageSize">Tamano de pagina.</param>
/// <param name="Search">Texto parcial para buscar por correo o nombre.</param>
/// <param name="PlanCode">Codigo exacto de plan para filtrar.</param>
/// <param name="SortBy">Campo de ordenamiento.</param>
/// <param name="SortDirection">Direccion del ordenamiento.</param>
public sealed record AdminUserListQuery(
    int Page,
    int PageSize,
    string? Search,
    string? PlanCode,
    string SortBy,
    string SortDirection);

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
/// Resultado paginado generico para contratos administrativos.
/// </summary>
/// <typeparam name="T">Tipo de elemento listado.</typeparam>
/// <param name="Items">Elementos de la pagina actual.</param>
/// <param name="Page">Pagina actual.</param>
/// <param name="PageSize">Tamano de pagina aplicado.</param>
/// <param name="TotalItems">Cantidad total de registros.</param>
/// <param name="TotalPages">Cantidad total de paginas.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

/// <summary>
/// Plan administrable expuesto para UI administrativa.
/// </summary>
/// <param name="PlanId">Identificador del plan.</param>
/// <param name="Code">Codigo tecnico del plan.</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="MaxGps">Cuota maxima de GPS.</param>
/// <param name="IsActive">Indica si el plan esta activo para asignacion.</param>
public sealed record AdminPlanDto(
    Guid PlanId,
    string Code,
    string Name,
    int MaxGps,
    bool IsActive);

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
