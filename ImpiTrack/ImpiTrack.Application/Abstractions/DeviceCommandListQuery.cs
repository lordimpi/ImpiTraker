namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Parametros de paginacion y filtrado para listar comandos de dispositivo.
/// </summary>
/// <param name="Status">Filtra por estado del ciclo de vida. Null para todos los estados.</param>
/// <param name="From">Filtra comandos encolados a partir de esta marca UTC. Null sin limite inferior.</param>
/// <param name="To">Filtra comandos encolados hasta esta marca UTC. Null sin limite superior.</param>
/// <param name="Page">Numero de pagina (base 1).</param>
/// <param name="PageSize">Cantidad de registros por pagina.</param>
public sealed record DeviceCommandListQuery(
    string? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 20);
