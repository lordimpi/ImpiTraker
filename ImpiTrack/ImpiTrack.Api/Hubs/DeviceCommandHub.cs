using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace ImpiTrack.Api.Hubs;

/// <summary>
/// Hub SignalR para el ciclo de vida de comandos de dispositivo en tiempo real. Unidireccional (servidor -> cliente).
/// Los clientes se agrupan por usuario: <c>user-{userId}</c>.
/// La autenticacion JWT se extrae de la query string <c>access_token</c> cuando aplica (configurado en Program.cs).
/// </summary>
[Authorize]
public sealed class DeviceCommandHub : Hub
{
    private readonly ILogger<DeviceCommandHub> _logger;

    /// <summary>
    /// Crea una instancia del hub de comandos de dispositivo.
    /// </summary>
    /// <param name="logger">Logger para eventos de conexion.</param>
    public DeviceCommandHub(ILogger<DeviceCommandHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Maneja la conexion de un nuevo cliente. Lo agrega al grupo <c>user-{userId}</c>.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        string? userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            string groupName = $"user-{userId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            _logger.LogInformation(
                "signalr_commands_connected connectionId={ConnectionId} userId={UserId} group={Group}",
                Context.ConnectionId,
                userId,
                groupName);
        }
        else
        {
            _logger.LogWarning(
                "signalr_commands_connected_no_user connectionId={ConnectionId}",
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
            "signalr_commands_disconnected connectionId={ConnectionId} exception={Exception}",
            Context.ConnectionId,
            exception?.Message ?? "none");

        return base.OnDisconnectedAsync(exception);
    }
}
