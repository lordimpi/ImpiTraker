using System.Text;
using Dapper;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Connection;
using Microsoft.Extensions.Logging;

namespace ImpiTrack.DataAccess.Repositories;

/// <summary>
/// Repositorio Dapper para el ciclo de vida de comandos de dispositivo.
/// Soporta SQL Server y PostgreSQL segun el <see cref="DatabaseRuntimeContext"/> inyectado.
/// </summary>
public sealed class DeviceCommandRepository : IDeviceCommandRepository
{
    /// <summary>
    /// Estados terminales del ciclo de vida. Una vez alcanzado alguno, ninguna
    /// transicion posterior debe sobrescribir el registro (proteccion de tamper).
    /// </summary>
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Acknowledged",
            "Failed",
            "Timeout",
            "InterruptedByRestart"
        };

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DatabaseRuntimeContext _context;
    private readonly ILogger<DeviceCommandRepository> _logger;

    /// <summary>
    /// Crea una nueva instancia del repositorio de comandos.
    /// </summary>
    public DeviceCommandRepository(
        IDbConnectionFactory connectionFactory,
        DatabaseRuntimeContext context,
        ILogger<DeviceCommandRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> InsertAsync(DeviceCommandRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO device_commands (
                command_id, imei, user_id, command_type, parameters,
                protocol, payload_sent, correlation_key, correlation_timestamp,
                status, queued_at_utc, sent_at_utc, acked_at_utc,
                response_code, response_text, failure_reason, user_ip, user_agent
            ) VALUES (
                @CommandId, @Imei, @UserId, @CommandType, @Parameters,
                @Protocol, @PayloadSent, @CorrelationKey, @CorrelationTimestamp,
                @Status, @QueuedAtUtc, @SentAtUtc, @AckedAtUtc,
                @ResponseCode, @ResponseText, @FailureReason, @UserIp, @UserAgent
            )
            """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            sql,
            new
            {
                record.CommandId,
                record.Imei,
                record.UserId,
                record.CommandType,
                record.Parameters,
                record.Protocol,
                record.PayloadSent,
                record.CorrelationKey,
                record.CorrelationTimestamp,
                record.Status,
                record.QueuedAtUtc,
                record.SentAtUtc,
                record.AckedAtUtc,
                record.ResponseCode,
                record.ResponseText,
                record.FailureReason,
                record.UserIp,
                record.UserAgent
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        return record.CommandId;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateStatusAsync(
        Guid commandId,
        string newStatus,
        DateTimeOffset? timestamp,
        string? payloadSent,
        string? correlationKey,
        string? correlationTimestamp,
        string? responseCode,
        string? responseText,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        // Tamper-resistance: read current status first; skip update if already terminal.
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        CommandDefinition readStatus = new(
            "SELECT status FROM device_commands WHERE command_id = @CommandId",
            new { CommandId = commandId },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        string? currentStatus = await connection.QuerySingleOrDefaultAsync<string>(readStatus);

        if (currentStatus is null)
        {
            _logger.LogWarning(
                "device_command_update_not_found command_id={CommandId} attempted_status={Status}",
                commandId, newStatus);
            return false;
        }

        if (TerminalStatuses.Contains(currentStatus))
        {
            _logger.LogWarning(
                "device_command_update_skipped_terminal command_id={CommandId} current_status={CurrentStatus} attempted_status={NewStatus}",
                commandId, currentStatus, newStatus);
            return false;
        }

        string sql = BuildUpdateSql(newStatus);
        CommandDefinition update = new(
            sql,
            new
            {
                CommandId = commandId,
                NewStatus = newStatus,
                Timestamp = timestamp,
                PayloadSent = payloadSent,
                CorrelationKey = correlationKey,
                CorrelationTimestamp = correlationTimestamp,
                ResponseCode = responseCode,
                ResponseText = responseText,
                FailureReason = failureReason
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int affected = await connection.ExecuteAsync(update);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<DeviceCommandRecord?> GetByIdAsync(Guid commandId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM device_commands WHERE command_id = @CommandId";

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            sql,
            new { CommandId = commandId },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<DeviceCommandRecord>(command);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceCommandRecord>> GetQueuedByImeiAsync(string imei, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT * FROM device_commands
            WHERE imei = @Imei AND status = 'Queued'
            ORDER BY queued_at_utc ASC
            """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            sql,
            new { Imei = imei },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<DeviceCommandRecord> rows = await connection.QueryAsync<DeviceCommandRecord>(command);
        return rows.ToArray();
    }

    /// <inheritdoc />
    public async Task<PagedResult<DeviceCommandRecord>> ListPagedAsync(
        string imei,
        Guid userId,
        DeviceCommandListQuery query,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        int offset = (page - 1) * pageSize;

        var parameters = new
        {
            Imei = imei,
            UserId = userId,
            Status = query.Status,
            From = query.From,
            To = query.To,
            Offset = offset,
            PageSize = pageSize
        };

        string whereSql = BuildListWhereClause(query);
        string countSql = $"SELECT COUNT(*) FROM device_commands WHERE imei = @Imei AND user_id = @UserId{whereSql}";
        string dataSql = BuildListDataSql(_context.Provider, whereSql);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        CommandDefinition countCommand = new(
            countSql,
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int totalItems = await connection.QuerySingleAsync<int>(countCommand);

        CommandDefinition dataCommand = new(
            dataSql,
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<DeviceCommandRecord> rows = await connection.QueryAsync<DeviceCommandRecord>(dataCommand);
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<DeviceCommandRecord>(rows.ToArray(), page, pageSize, totalItems, totalPages);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceCommandRecord>> ScanStaleSentAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT * FROM device_commands
            WHERE status = 'Sent' AND sent_at_utc < @OlderThan
            ORDER BY sent_at_utc ASC
            """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            sql,
            new { OlderThan = olderThan },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<DeviceCommandRecord> rows = await connection.QueryAsync<DeviceCommandRecord>(command);
        return rows.ToArray();
    }

    /// <inheritdoc />
    public async Task<DeviceCommandRecord?> MatchPendingByKeywordAsync(
        string imei,
        string correlationKey,
        CancellationToken cancellationToken)
    {
        // Coban FIFO: select the oldest Sent command with matching correlation_key for this IMEI.
        string sql = $"""
            SELECT * FROM device_commands
            WHERE imei = @Imei AND status = 'Sent' AND correlation_key = @CorrelationKey
            ORDER BY sent_at_utc ASC
            {GetLimitOneSql(_context.Provider)}
            """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            sql,
            new { Imei = imei, CorrelationKey = correlationKey },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<DeviceCommandRecord>(command);
    }

    /// <inheritdoc />
    public async Task<DeviceCommandRecord?> MatchPendingByCorrelationAsync(
        string imei,
        string correlationKey,
        string correlationTimestamp,
        CancellationToken cancellationToken)
    {
        // Cantrack exact: match both correlation_key (cmd) and correlation_timestamp (hhmmss).
        string sql = $"""
            SELECT * FROM device_commands
            WHERE imei = @Imei AND status = 'Sent'
              AND correlation_key = @CorrelationKey
              AND correlation_timestamp = @CorrelationTimestamp
            {GetLimitOneSql(_context.Provider)}
            """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            sql,
            new { Imei = imei, CorrelationKey = correlationKey, CorrelationTimestamp = correlationTimestamp },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<DeviceCommandRecord>(command);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string BuildUpdateSql(string newStatus)
    {
        // Determine which timestamp column to populate based on target status.
        string timestampCol = newStatus switch
        {
            "Sent" => "sent_at_utc = @Timestamp,",
            "Acknowledged" or "Failed" or "Timeout" or "InterruptedByRestart" => "acked_at_utc = @Timestamp,",
            _ => string.Empty
        };

        return $"""
            UPDATE device_commands SET
                status = @NewStatus,
                {timestampCol}
                payload_sent      = COALESCE(@PayloadSent, payload_sent),
                correlation_key   = COALESCE(@CorrelationKey, correlation_key),
                correlation_timestamp = COALESCE(@CorrelationTimestamp, correlation_timestamp),
                response_code     = COALESCE(@ResponseCode, response_code),
                response_text     = COALESCE(@ResponseText, response_text),
                failure_reason    = COALESCE(@FailureReason, failure_reason)
            WHERE command_id = @CommandId
            """;
    }

    private static string BuildListWhereClause(DeviceCommandListQuery query)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(query.Status))
            sb.Append(" AND status = @Status");
        if (query.From.HasValue)
            sb.Append(" AND queued_at_utc >= @From");
        if (query.To.HasValue)
            sb.Append(" AND queued_at_utc <= @To");
        return sb.ToString();
    }

    private static string BuildListDataSql(DatabaseProvider provider, string whereSql)
    {
        if (provider == DatabaseProvider.Postgres)
        {
            return $"""
                SELECT * FROM device_commands
                WHERE imei = @Imei AND user_id = @UserId{whereSql}
                ORDER BY queued_at_utc DESC
                LIMIT @PageSize OFFSET @Offset
                """;
        }

        // SQL Server requires ORDER BY before OFFSET/FETCH
        return $"""
            SELECT * FROM device_commands
            WHERE imei = @Imei AND user_id = @UserId{whereSql}
            ORDER BY queued_at_utc DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;
    }

    private static string GetLimitOneSql(DatabaseProvider provider) =>
        provider == DatabaseProvider.Postgres ? "LIMIT 1" : "FETCH FIRST 1 ROWS ONLY";
}
