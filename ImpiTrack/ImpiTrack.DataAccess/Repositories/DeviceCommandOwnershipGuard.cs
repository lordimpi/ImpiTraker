using Dapper;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Connection;

namespace ImpiTrack.DataAccess.Repositories;

/// <summary>
/// Verifica si un usuario es propietario activo de un IMEI consultando <c>user_devices</c>.
/// </summary>
public sealed class DeviceCommandOwnershipGuard : IDeviceCommandOwnershipGuard
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DatabaseRuntimeContext _context;

    /// <summary>Crea un nuevo guardian de ownership.</summary>
    public DeviceCommandOwnershipGuard(
        IDbConnectionFactory connectionFactory,
        DatabaseRuntimeContext context)
    {
        _connectionFactory = connectionFactory;
        _context = context;
    }

    /// <inheritdoc />
    public async Task<bool> IsOwnerAsync(string imei, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imei)) return false;

        string sql = _context.Provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM user_devices WHERE imei = @Imei AND user_id = @UserId AND is_active = 1"
            : "SELECT COUNT(*) FROM user_devices WHERE imei = @Imei AND user_id = @UserId AND is_active = TRUE";

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            sql,
            new { Imei = imei, UserId = userId },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int count = await connection.ExecuteScalarAsync<int>(command);
        return count > 0;
    }
}
