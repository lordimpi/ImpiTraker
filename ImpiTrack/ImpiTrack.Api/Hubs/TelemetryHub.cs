using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ImpiTrack.Api.Hubs;

/// <summary>
/// Hub SignalR para telemetria en tiempo real. Unidireccional (servidor → cliente).
/// Los clientes se agrupan por usuario: <c>user_{userId}</c>.
/// </summary>
[Authorize]
public sealed class TelemetryHub : Hub
{
    private readonly ILogger<TelemetryHub> _logger;

    /// <summary>
    /// Crea una instancia del hub de telemetria.
    /// </summary>
    /// <param name="logger">Logger para eventos de conexion.</param>
    public TelemetryHub(ILogger<TelemetryHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Maneja la conexion de un nuevo cliente. Lo agrega al grupo <c>user_{userId}</c>.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        string? userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            string groupName = $"user_{userId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            _logger.LogInformation(
                "signalr_connected connectionId={ConnectionId} userId={UserId} group={Group}",
                Context.ConnectionId,
                userId,
                groupName);
        }
        else
        {
            _logger.LogWarning(
                "signalr_connected_no_user connectionId={ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Maneja la desconexion de un cliente. SignalR remueve automaticamente del grupo.
    /// </summary>
    /// <param name="exception">Excepcion que provoco la desconexion, si existe.</param>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "signalr_disconnected connectionId={ConnectionId} exception={Exception}",
            Context.ConnectionId,
            exception?.Message ?? "none");

        return base.OnDisconnectedAsync(exception);
    }
}
