using System.Data;
using Dapper;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Connection;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Queue;

namespace ImpiTrack.DataAccess.Repositories;

/// <summary>
/// Repositorio SQL con soporte para SQL Server y PostgreSQL.
/// </summary>
public sealed class SqlDataRepository : IOpsRepository, IIngestionRepository, IUserAccountRepository
{
    private const string DefaultPlanCode = "BASIC";
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DatabaseRuntimeContext _context;

    /// <summary>
    /// Crea un repositorio SQL para ingesta y consultas operativas.
    /// </summary>
    /// <param name="connectionFactory">Fabrica de conexiones abiertas.</param>
    /// <param name="context">Contexto de configuracion del proveedor.</param>
    public SqlDataRepository(IDbConnectionFactory connectionFactory, DatabaseRuntimeContext context)
    {
        _connectionFactory = connectionFactory;
        _context = context;
    }

    /// <inheritdoc />
    public async Task AddRawPacketAsync(RawPacketRecord record, long backlog, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            Guid? deviceId = await ResolveDeviceIdAsync(
                connection,
                transaction,
                record.Imei,
                cancellationToken);

            CommandDefinition insertRaw = new(
                GetInsertRawPacketSql(_context.Provider),
                new
                {
                    PacketId = record.PacketId.Value,
                    SessionId = record.SessionId.Value,
                    DeviceId = deviceId,
                    Imei = record.Imei,
                    Port = record.Port,
                    RemoteIp = record.RemoteIp,
                    Protocol = (int)record.Protocol,
                    MessageType = (int)record.MessageType,
                    PayloadText = record.PayloadText,
                    ReceivedAtUtc = record.ReceivedAtUtc,
                    ParseStatus = (int)record.ParseStatus,
                    ParseError = record.ParseError,
                    AckSent = record.AckSent,
                    AckPayload = record.AckPayload,
                    AckAtUtc = record.AckAtUtc,
                    AckLatencyMs = record.AckLatencyMs,
                    QueueBacklog = backlog
                },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(insertRaw);

            CommandDefinition upsertSnapshot = new(
                GetUpsertPortSnapshotSql(_context.Provider),
                new
                {
                    record.Port,
                    ParseOkDelta = record.ParseStatus == RawParseStatus.Ok ? 1 : 0,
                    ParseFailDelta = record.ParseStatus == RawParseStatus.Ok ? 0 : 1,
                    AckDelta = record.AckSent ? 1 : 0,
                    QueueBacklog = backlog,
                    ReceivedAtUtc = record.ReceivedAtUtc
                },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(upsertSnapshot);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpsertSessionAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            Guid? deviceId = await ResolveDeviceIdAsync(
                connection,
                transaction,
                session.Imei,
                cancellationToken);

            CommandDefinition command = new(
                GetUpsertSessionSql(_context.Provider),
                new
                {
                    SessionId = session.SessionId.Value,
                    DeviceId = deviceId,
                    Imei = session.Imei,
                    session.RemoteIp,
                    session.Port,
                    session.ConnectedAtUtc,
                    session.LastSeenAtUtc,
                    session.LastHeartbeatAtUtc,
                    session.FramesIn,
                    session.FramesInvalid,
                    session.CloseReason,
                    session.DisconnectedAtUtc,
                    session.IsActive
                },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PersistEnvelopeAsync(InboundEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            Guid? deviceId = await ResolveDeviceIdAsync(
                connection,
                transaction,
                envelope.Message.Imei,
                cancellationToken);

            if (deviceId is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (envelope.Message.MessageType == MessageType.Tracking)
            {
                CommandDefinition insertPosition = new(
                    GetInsertPositionSql(),
                    new
                    {
                        PositionId = Guid.NewGuid(),
                        PacketId = envelope.PacketId.Value,
                        SessionId = envelope.SessionId.Value,
                        DeviceId = deviceId,
                        Imei = envelope.Message.Imei,
                        Protocol = (int)envelope.Message.Protocol,
                        MessageType = (int)envelope.Message.MessageType,
                        GpsTimeUtc = envelope.Message.GpsTimeUtc ?? envelope.Message.ReceivedAtUtc,
                        Latitude = envelope.Message.Latitude is null
                            ? (decimal?)null
                            : Convert.ToDecimal(envelope.Message.Latitude.Value),
                        Longitude = envelope.Message.Longitude is null
                            ? (decimal?)null
                            : Convert.ToDecimal(envelope.Message.Longitude.Value),
                        SpeedKmh = envelope.Message.SpeedKmh,
                        HeadingDeg = envelope.Message.HeadingDeg,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    },
                    transaction,
                    _context.CommandTimeoutSeconds,
                    cancellationToken: cancellationToken);

                await connection.ExecuteAsync(insertPosition);
            }
            else
            {
                CommandDefinition insertEvent = new(
                    GetInsertEventSql(),
                    new
                    {
                        EventId = Guid.NewGuid(),
                        PacketId = envelope.PacketId.Value,
                        SessionId = envelope.SessionId.Value,
                        DeviceId = deviceId,
                        Imei = envelope.Message.Imei,
                        Protocol = (int)envelope.Message.Protocol,
                        MessageType = (int)envelope.Message.MessageType,
                        EventCode = envelope.Message.MessageType.ToString(),
                        PayloadText = envelope.Message.Text,
                        ReceivedAtUtc = envelope.Message.ReceivedAtUtc
                    },
                    transaction,
                    _context.CommandTimeoutSeconds,
                    cancellationToken: cancellationToken);

                await connection.ExecuteAsync(insertEvent);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RawPacketRecord>> GetLatestRawPacketsAsync(
        string? imei,
        int limit,
        CancellationToken cancellationToken)
    {
        int normalizedLimit = Math.Clamp(limit, 1, 500);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        CommandDefinition command = new(
            GetLatestRawPacketsSql(_context.Provider),
            new { Imei = imei, Limit = normalizedLimit },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<RawPacketRow> rows = await connection.QueryAsync<RawPacketRow>(command);
        return rows.Select(ToRawPacketRecord).ToArray();
    }

    /// <inheritdoc />
    public async Task<RawPacketRecord?> GetRawPacketByIdAsync(PacketId packetId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        CommandDefinition command = new(
            GetRawPacketByIdSql(),
            new { PacketId = packetId.Value },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        RawPacketRow? row = await connection.QuerySingleOrDefaultAsync<RawPacketRow>(command);
        return row is null ? null : ToRawPacketRecord(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ErrorAggregate>> GetTopErrorsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string groupBy,
        int limit,
        CancellationToken cancellationToken)
    {
        int normalizedLimit = Math.Clamp(limit, 1, 200);
        string grouping = NormalizeGrouping(groupBy);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetTopErrorsSql(grouping, _context.Provider),
            new { FromUtc = fromUtc, ToUtc = toUtc, Limit = normalizedLimit },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<ErrorAggregateRow> rows = await connection.QueryAsync<ErrorAggregateRow>(command);
        return rows
            .Select(x => new ErrorAggregate(x.GroupKey, x.Count, x.LastPacketId.HasValue ? new PacketId(x.LastPacketId.Value) : null))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionRecord>> GetActiveSessionsAsync(int? port, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetActiveSessionsSql(_context.Provider),
            new { Port = port },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<SessionRow> rows = await connection.QueryAsync<SessionRow>(command);
        return rows.Select(ToSessionRecord).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PortIngestionSnapshot>> GetPortSnapshotsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetPortSnapshotsSql(_context.Provider),
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<PortSnapshotRow> rows = await connection.QueryAsync<PortSnapshotRow>(command);
        return rows
            .Select(x => new PortIngestionSnapshot(
                x.Port,
                x.ActiveConnections,
                x.FramesIn,
                x.ParseOk,
                x.ParseFail,
                x.AckSent,
                x.Backlog))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task EnsureUserProvisioningAsync(
        Guid userId,
        string email,
        string? fullName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            CommandDefinition upsertProfile = new(
                GetUpsertUserProfileSql(_context.Provider),
                new
                {
                    UserId = userId,
                    Email = email,
                    FullName = fullName,
                    NowUtc = nowUtc
                },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(upsertProfile);

            CommandDefinition ensureSubscription = new(
                GetEnsureDefaultSubscriptionSql(_context.Provider),
                new
                {
                    UserId = userId,
                    PlanCode = DefaultPlanCode,
                    NowUtc = nowUtc,
                    SubscriptionId = Guid.NewGuid()
                },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(ensureSubscription);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<UserAccountSummary?> GetUserSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetUserSummarySql(_context.Provider),
            new { UserId = userId },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        UserSummaryRow? row = await connection.QuerySingleOrDefaultAsync<UserSummaryRow>(command);
        if (row is null)
        {
            return null;
        }

        return new UserAccountSummary(
            row.UserId,
            row.Email,
            row.FullName,
            row.PlanCode,
            row.PlanName,
            row.MaxGps,
            row.UsedGps);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserDeviceBinding>> GetUserDevicesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetUserDevicesSql(_context.Provider),
            new { UserId = userId },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<UserDeviceRow> rows = await connection.QueryAsync<UserDeviceRow>(command);
        return rows
            .Select(x => new UserDeviceBinding(x.DeviceId, x.Imei, x.BoundAtUtc))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<BindDeviceResult> BindDeviceAsync(
        Guid userId,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        string normalizedImei = imei.Trim();
        if (normalizedImei.Length == 0)
        {
            return new BindDeviceResult(BindDeviceStatus.OwnedByAnotherUser, null);
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            CommandDefinition ownerCommand = new(
                GetActiveOwnerByImeiSql(_context.Provider),
                new { Imei = normalizedImei },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            DeviceOwnerRow? owner = await connection.QuerySingleOrDefaultAsync<DeviceOwnerRow>(ownerCommand);
            if (owner is not null)
            {
                if (owner.UserId == userId)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new BindDeviceResult(BindDeviceStatus.AlreadyBound, owner.DeviceId);
                }

                await transaction.CommitAsync(cancellationToken);
                return new BindDeviceResult(BindDeviceStatus.OwnedByAnotherUser, null);
            }

            CommandDefinition quotaCommand = new(
                GetUserQuotaSql(_context.Provider),
                new { UserId = userId },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            UserQuotaRow? quota = await connection.QuerySingleOrDefaultAsync<UserQuotaRow>(quotaCommand);
            if (quota is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new BindDeviceResult(BindDeviceStatus.MissingActivePlan, null);
            }

            if (quota.UsedGps >= quota.MaxGps)
            {
                await transaction.CommitAsync(cancellationToken);
                return new BindDeviceResult(BindDeviceStatus.QuotaExceeded, null);
            }

            Guid deviceId = await EnsureDeviceAsync(connection, transaction, normalizedImei, nowUtc, cancellationToken);

            CommandDefinition bindCommand = new(
                GetBindUserDeviceSql(_context.Provider),
                new
                {
                    UserDeviceId = Guid.NewGuid(),
                    UserId = userId,
                    DeviceId = deviceId,
                    Imei = normalizedImei,
                    BoundAtUtc = nowUtc
                },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(bindCommand);
            await transaction.CommitAsync(cancellationToken);
            return new BindDeviceResult(BindDeviceStatus.Bound, deviceId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnbindDeviceAsync(
        Guid userId,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetUnbindUserDeviceSql(_context.Provider),
            new
            {
                UserId = userId,
                Imei = imei.Trim(),
                NowUtc = nowUtc
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int affected = await connection.ExecuteAsync(command);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAccountOverview>> GetUsersAsync(int limit, CancellationToken cancellationToken)
    {
        int normalizedLimit = Math.Clamp(limit, 1, 500);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetUsersOverviewSql(_context.Provider),
            new { Limit = normalizedLimit },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<UserOverviewRow> rows = await connection.QueryAsync<UserOverviewRow>(command);
        return rows.Select(x => new UserAccountOverview(
            x.UserId,
            x.Email,
            x.FullName,
            x.PlanCode,
            x.MaxGps,
            x.UsedGps)).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> SetUserPlanAsync(
        Guid userId,
        string planCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            CommandDefinition resolvePlan = new(
                GetPlanIdByCodeSql(_context.Provider),
                new { PlanCode = planCode.Trim().ToUpperInvariant() },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            Guid? planId = await connection.ExecuteScalarAsync<Guid?>(resolvePlan);
            if (planId is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            CommandDefinition deactivate = new(
                GetDeactivateActiveSubscriptionsSql(_context.Provider),
                new { UserId = userId, NowUtc = nowUtc },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(deactivate);

            CommandDefinition insert = new(
                GetInsertSubscriptionSql(_context.Provider),
                new
                {
                    SubscriptionId = Guid.NewGuid(),
                    UserId = userId,
                    PlanId = planId.Value,
                    StartsAtUtc = nowUtc,
                    CreatedAtUtc = nowUtc
                },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            int affected = await connection.ExecuteAsync(insert);
            await transaction.CommitAsync(cancellationToken);
            return affected > 0;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<Guid> EnsureDeviceAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        CommandDefinition command = new(
            GetEnsureDeviceSql(_context.Provider),
            new
            {
                DeviceId = Guid.NewGuid(),
                Imei = imei,
                NowUtc = nowUtc
            },
            transaction,
            _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        Guid? deviceId = await connection.ExecuteScalarAsync<Guid?>(command);
        if (!deviceId.HasValue)
        {
            throw new InvalidOperationException("ensure_device_failed");
        }

        return deviceId.Value;
    }

    private async Task<Guid?> ResolveDeviceIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string? imei,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            return null;
        }

        CommandDefinition command = new(
            GetOwnedDeviceByImeiSql(_context.Provider),
            new
            {
                Imei = imei
            },
            transaction,
            _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<Guid?>(command);
    }

    private static RawPacketRecord ToRawPacketRecord(RawPacketRow row)
    {
        ProtocolId protocol = Enum.IsDefined(typeof(ProtocolId), row.Protocol)
            ? (ProtocolId)row.Protocol
            : ProtocolId.Unknown;

        MessageType messageType = Enum.IsDefined(typeof(MessageType), row.MessageType)
            ? (MessageType)row.MessageType
            : MessageType.Unknown;

        RawParseStatus parseStatus = Enum.IsDefined(typeof(RawParseStatus), row.ParseStatus)
            ? (RawParseStatus)row.ParseStatus
            : RawParseStatus.Failed;

        return new RawPacketRecord(
            new SessionId(row.SessionId),
            new PacketId(row.PacketId),
            row.Port,
            row.RemoteIp,
            protocol,
            row.Imei,
            messageType,
            row.PayloadText,
            row.ReceivedAtUtc,
            parseStatus,
            row.ParseError,
            row.AckSent,
            row.AckPayload,
            row.AckAtUtc,
            row.AckLatencyMs);
    }

    private static SessionRecord ToSessionRecord(SessionRow row)
    {
        return new SessionRecord(
            new SessionId(row.SessionId),
            row.RemoteIp,
            row.Port,
            row.ConnectedAtUtc,
            row.LastSeenAtUtc,
            row.LastHeartbeatAtUtc,
            row.Imei,
            row.FramesIn,
            row.FramesInvalid,
            row.CloseReason,
            row.DisconnectedAtUtc,
            row.IsActive);
    }

    private static string NormalizeGrouping(string groupBy)
    {
        if (string.Equals(groupBy, "protocol", StringComparison.OrdinalIgnoreCase))
        {
            return "protocol";
        }

        if (string.Equals(groupBy, "port", StringComparison.OrdinalIgnoreCase))
        {
            return "port";
        }

        return "errorCode";
    }

    private static string GetInsertPositionSql()
    {
        return
            """
            INSERT INTO positions
            (
                position_id, packet_id, session_id, device_id, imei, protocol, message_type,
                gps_time_utc, latitude, longitude, speed_kmh, heading_deg, created_at_utc
            )
            VALUES
            (
                @PositionId, @PacketId, @SessionId, @DeviceId, @Imei, @Protocol, @MessageType,
                @GpsTimeUtc, @Latitude, @Longitude, @SpeedKmh, @HeadingDeg, @CreatedAtUtc
            );
            """;
    }

    private static string GetInsertEventSql()
    {
        return
            """
            INSERT INTO device_events
            (
                event_id, packet_id, session_id, device_id, imei, protocol, message_type,
                event_code, payload_text, received_at_utc
            )
            VALUES
            (
                @EventId, @PacketId, @SessionId, @DeviceId, @Imei, @Protocol, @MessageType,
                @EventCode, @PayloadText, @ReceivedAtUtc
            );
            """;
    }

    private static string GetLatestRawPacketsSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP (@Limit)
                    packet_id AS PacketId,
                    session_id AS SessionId,
                    port AS Port,
                    remote_ip AS RemoteIp,
                    protocol AS Protocol,
                    imei AS Imei,
                    message_type AS MessageType,
                    payload_text AS PayloadText,
                    received_at_utc AS ReceivedAtUtc,
                    parse_status AS ParseStatus,
                    parse_error AS ParseError,
                    ack_sent AS AckSent,
                    ack_payload AS AckPayload,
                    ack_at_utc AS AckAtUtc,
                    ack_latency_ms AS AckLatencyMs
                FROM raw_packets
                WHERE (@Imei IS NULL OR imei = @Imei)
                ORDER BY received_at_utc DESC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    packet_id AS "PacketId",
                    session_id AS "SessionId",
                    port AS "Port",
                    remote_ip AS "RemoteIp",
                    protocol AS "Protocol",
                    imei AS "Imei",
                    message_type AS "MessageType",
                    payload_text AS "PayloadText",
                    received_at_utc AS "ReceivedAtUtc",
                    parse_status AS "ParseStatus",
                    parse_error AS "ParseError",
                    ack_sent AS "AckSent",
                    ack_payload AS "AckPayload",
                    ack_at_utc AS "AckAtUtc",
                    ack_latency_ms AS "AckLatencyMs"
                FROM raw_packets
                WHERE (@Imei IS NULL OR imei = @Imei)
                ORDER BY received_at_utc DESC
                LIMIT @Limit;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetRawPacketByIdSql()
    {
        return
            """
            SELECT
                packet_id AS PacketId,
                session_id AS SessionId,
                port AS Port,
                remote_ip AS RemoteIp,
                protocol AS Protocol,
                imei AS Imei,
                message_type AS MessageType,
                payload_text AS PayloadText,
                received_at_utc AS ReceivedAtUtc,
                parse_status AS ParseStatus,
                parse_error AS ParseError,
                ack_sent AS AckSent,
                ack_payload AS AckPayload,
                ack_at_utc AS AckAtUtc,
                ack_latency_ms AS AckLatencyMs
            FROM raw_packets
            WHERE packet_id = @PacketId;
            """;
    }

    private static string GetTopErrorsSql(string grouping, DatabaseProvider provider)
    {
        string groupExpression = grouping switch
        {
            "protocol" => provider == DatabaseProvider.Postgres
                ? "CAST(protocol AS TEXT)"
                : "CAST(protocol AS NVARCHAR(32))",
            "port" => provider == DatabaseProvider.Postgres
                ? "CAST(port AS TEXT)"
                : "CAST(port AS NVARCHAR(32))",
            _ => "COALESCE(parse_error, 'unknown_error')"
        };

        return provider switch
        {
            DatabaseProvider.SqlServer =>
                $"""
                SELECT TOP (@Limit)
                    {groupExpression} AS GroupKey,
                    COUNT_BIG(*) AS Count,
                    MAX(packet_id) AS LastPacketId
                FROM raw_packets
                WHERE parse_status <> 1
                  AND received_at_utc >= @FromUtc
                  AND received_at_utc <= @ToUtc
                GROUP BY {groupExpression}
                ORDER BY Count DESC, GroupKey ASC;
                """,
            DatabaseProvider.Postgres =>
                $"""
                SELECT
                    {groupExpression} AS "GroupKey",
                    COUNT(*)::BIGINT AS "Count",
                    MAX(packet_id) AS "LastPacketId"
                FROM raw_packets
                WHERE parse_status <> 1
                  AND received_at_utc >= @FromUtc
                  AND received_at_utc <= @ToUtc
                GROUP BY {groupExpression}
                ORDER BY "Count" DESC, "GroupKey" ASC
                LIMIT @Limit;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetActiveSessionsSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    session_id AS SessionId,
                    remote_ip AS RemoteIp,
                    port AS Port,
                    connected_at_utc AS ConnectedAtUtc,
                    last_seen_at_utc AS LastSeenAtUtc,
                    last_heartbeat_at_utc AS LastHeartbeatAtUtc,
                    imei AS Imei,
                    frames_in AS FramesIn,
                    frames_invalid AS FramesInvalid,
                    close_reason AS CloseReason,
                    disconnected_at_utc AS DisconnectedAtUtc,
                    is_active AS IsActive
                FROM device_sessions
                WHERE is_active = 1
                  AND (@Port IS NULL OR port = @Port)
                ORDER BY last_seen_at_utc DESC, session_id ASC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    session_id AS "SessionId",
                    remote_ip AS "RemoteIp",
                    port AS "Port",
                    connected_at_utc AS "ConnectedAtUtc",
                    last_seen_at_utc AS "LastSeenAtUtc",
                    last_heartbeat_at_utc AS "LastHeartbeatAtUtc",
                    imei AS "Imei",
                    frames_in AS "FramesIn",
                    frames_invalid AS "FramesInvalid",
                    close_reason AS "CloseReason",
                    disconnected_at_utc AS "DisconnectedAtUtc",
                    is_active AS "IsActive"
                FROM device_sessions
                WHERE is_active = TRUE
                  AND (@Port IS NULL OR port = @Port)
                ORDER BY last_seen_at_utc DESC, session_id ASC;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetPortSnapshotsSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                WITH active AS
                (
                    SELECT port, COUNT_BIG(*) AS active_connections
                    FROM device_sessions
                    WHERE is_active = 1
                    GROUP BY port
                )
                SELECT
                    s.port AS Port,
                    CAST(ISNULL(a.active_connections, 0) AS INT) AS ActiveConnections,
                    s.frames_in AS FramesIn,
                    s.parse_ok AS ParseOk,
                    s.parse_fail AS ParseFail,
                    s.ack_sent AS AckSent,
                    s.backlog AS Backlog
                FROM port_ingestion_snapshots s
                LEFT JOIN active a ON a.port = s.port
                ORDER BY s.port ASC;
                """,
            DatabaseProvider.Postgres =>
                """
                WITH active AS
                (
                    SELECT port, COUNT(*)::BIGINT AS active_connections
                    FROM device_sessions
                    WHERE is_active = TRUE
                    GROUP BY port
                )
                SELECT
                    s.port AS "Port",
                    COALESCE(a.active_connections, 0)::INT AS "ActiveConnections",
                    s.frames_in AS "FramesIn",
                    s.parse_ok AS "ParseOk",
                    s.parse_fail AS "ParseFail",
                    s.ack_sent AS "AckSent",
                    s.backlog AS "Backlog"
                FROM port_ingestion_snapshots s
                LEFT JOIN active a ON a.port = s.port
                ORDER BY s.port ASC;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetOwnedDeviceByImeiSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP 1 ud.device_id
                FROM user_devices ud
                WHERE ud.imei = @Imei
                  AND ud.is_active = 1;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT ud.device_id
                FROM user_devices ud
                WHERE ud.imei = @Imei
                  AND ud.is_active = TRUE
                LIMIT 1;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetUpsertUserProfileSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF EXISTS (SELECT 1 FROM user_profiles WHERE user_id = @UserId)
                BEGIN
                    UPDATE user_profiles
                    SET email = @Email,
                        full_name = COALESCE(@FullName, full_name),
                        updated_at_utc = @NowUtc
                    WHERE user_id = @UserId;
                END
                ELSE
                BEGIN
                    INSERT INTO user_profiles
                    (
                        user_id, email, full_name, created_at_utc, updated_at_utc
                    )
                    VALUES
                    (
                        @UserId, @Email, @FullName, @NowUtc, @NowUtc
                    );
                END;
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO user_profiles
                (
                    user_id, email, full_name, created_at_utc, updated_at_utc
                )
                VALUES
                (
                    @UserId, @Email, @FullName, @NowUtc, @NowUtc
                )
                ON CONFLICT (user_id)
                DO UPDATE SET
                    email = EXCLUDED.email,
                    full_name = COALESCE(EXCLUDED.full_name, user_profiles.full_name),
                    updated_at_utc = EXCLUDED.updated_at_utc;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_upsert")
        };
    }

    private static string GetEnsureDefaultSubscriptionSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM user_plan_subscriptions
                    WHERE user_id = @UserId
                      AND status = 'Active'
                      AND ends_at_utc IS NULL
                )
                BEGIN
                    INSERT INTO user_plan_subscriptions
                    (
                        subscription_id, user_id, plan_id, status, starts_at_utc, ends_at_utc, created_at_utc
                    )
                    SELECT
                        @SubscriptionId, @UserId, p.plan_id, 'Active', @NowUtc, NULL, @NowUtc
                    FROM plans p
                    WHERE p.code = @PlanCode
                      AND p.is_active = 1;
                END;
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO user_plan_subscriptions
                (
                    subscription_id, user_id, plan_id, status, starts_at_utc, ends_at_utc, created_at_utc
                )
                SELECT
                    @SubscriptionId, @UserId, p.plan_id, 'Active', @NowUtc, NULL, @NowUtc
                FROM plans p
                WHERE p.code = @PlanCode
                  AND p.is_active = TRUE
                  AND NOT EXISTS
                  (
                    SELECT 1
                    FROM user_plan_subscriptions s
                    WHERE s.user_id = @UserId
                      AND s.status = 'Active'
                      AND s.ends_at_utc IS NULL
                  );
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_insert")
        };
    }

    private static string GetUserSummarySql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    up.user_id AS UserId,
                    up.email AS Email,
                    up.full_name AS FullName,
                    p.code AS PlanCode,
                    p.name AS PlanName,
                    p.max_gps AS MaxGps,
                    CAST(ISNULL(usage_data.used_gps, 0) AS INT) AS UsedGps
                FROM user_profiles up
                INNER JOIN user_plan_subscriptions s
                    ON s.user_id = up.user_id
                   AND s.status = 'Active'
                   AND s.ends_at_utc IS NULL
                INNER JOIN plans p
                    ON p.plan_id = s.plan_id
                OUTER APPLY
                (
                    SELECT COUNT_BIG(*) AS used_gps
                    FROM user_devices ud
                    WHERE ud.user_id = up.user_id
                      AND ud.is_active = 1
                ) usage_data
                WHERE up.user_id = @UserId;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    up.user_id AS "UserId",
                    up.email AS "Email",
                    up.full_name AS "FullName",
                    p.code AS "PlanCode",
                    p.name AS "PlanName",
                    p.max_gps AS "MaxGps",
                    COALESCE(usage_data.used_gps, 0)::INT AS "UsedGps"
                FROM user_profiles up
                INNER JOIN user_plan_subscriptions s
                    ON s.user_id = up.user_id
                   AND s.status = 'Active'
                   AND s.ends_at_utc IS NULL
                INNER JOIN plans p
                    ON p.plan_id = s.plan_id
                LEFT JOIN LATERAL
                (
                    SELECT COUNT(*)::BIGINT AS used_gps
                    FROM user_devices ud
                    WHERE ud.user_id = up.user_id
                      AND ud.is_active = TRUE
                ) usage_data ON TRUE
                WHERE up.user_id = @UserId;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetUserDevicesSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    device_id AS DeviceId,
                    imei AS Imei,
                    bound_at_utc AS BoundAtUtc
                FROM user_devices
                WHERE user_id = @UserId
                  AND is_active = 1
                ORDER BY bound_at_utc DESC, imei ASC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    device_id AS "DeviceId",
                    imei AS "Imei",
                    bound_at_utc AS "BoundAtUtc"
                FROM user_devices
                WHERE user_id = @UserId
                  AND is_active = TRUE
                ORDER BY bound_at_utc DESC, imei ASC;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetActiveOwnerByImeiSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP 1
                    user_id AS UserId,
                    device_id AS DeviceId
                FROM user_devices
                WHERE imei = @Imei
                  AND is_active = 1;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    user_id AS "UserId",
                    device_id AS "DeviceId"
                FROM user_devices
                WHERE imei = @Imei
                  AND is_active = TRUE
                LIMIT 1;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetUserQuotaSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    p.max_gps AS MaxGps,
                    CAST(ISNULL(usage_data.used_gps, 0) AS INT) AS UsedGps
                FROM user_plan_subscriptions s
                INNER JOIN plans p
                    ON p.plan_id = s.plan_id
                OUTER APPLY
                (
                    SELECT COUNT_BIG(*) AS used_gps
                    FROM user_devices ud
                    WHERE ud.user_id = s.user_id
                      AND ud.is_active = 1
                ) usage_data
                WHERE s.user_id = @UserId
                  AND s.status = 'Active'
                  AND s.ends_at_utc IS NULL;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    p.max_gps AS "MaxGps",
                    COALESCE(usage_data.used_gps, 0)::INT AS "UsedGps"
                FROM user_plan_subscriptions s
                INNER JOIN plans p
                    ON p.plan_id = s.plan_id
                LEFT JOIN LATERAL
                (
                    SELECT COUNT(*)::BIGINT AS used_gps
                    FROM user_devices ud
                    WHERE ud.user_id = s.user_id
                      AND ud.is_active = TRUE
                ) usage_data ON TRUE
                WHERE s.user_id = @UserId
                  AND s.status = 'Active'
                  AND s.ends_at_utc IS NULL;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetEnsureDeviceSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF EXISTS (SELECT 1 FROM devices WHERE imei = @Imei)
                BEGIN
                    UPDATE devices
                    SET last_seen_at_utc = @NowUtc
                    WHERE imei = @Imei;
                END
                ELSE
                BEGIN
                    INSERT INTO devices(device_id, imei, created_at_utc, last_seen_at_utc)
                    VALUES (@DeviceId, @Imei, @NowUtc, @NowUtc);
                END;

                SELECT device_id FROM devices WHERE imei = @Imei;
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO devices(device_id, imei, created_at_utc, last_seen_at_utc)
                VALUES (@DeviceId, @Imei, @NowUtc, @NowUtc)
                ON CONFLICT (imei)
                DO UPDATE SET last_seen_at_utc = EXCLUDED.last_seen_at_utc;

                SELECT device_id FROM devices WHERE imei = @Imei;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_upsert")
        };
    }

    private static string GetBindUserDeviceSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                INSERT INTO user_devices
                (
                    user_device_id, user_id, device_id, imei, bound_at_utc, unbound_at_utc, is_active
                )
                VALUES
                (
                    @UserDeviceId, @UserId, @DeviceId, @Imei, @BoundAtUtc, NULL, 1
                );
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO user_devices
                (
                    user_device_id, user_id, device_id, imei, bound_at_utc, unbound_at_utc, is_active
                )
                VALUES
                (
                    @UserDeviceId, @UserId, @DeviceId, @Imei, @BoundAtUtc, NULL, TRUE
                );
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_insert")
        };
    }

    private static string GetUnbindUserDeviceSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                UPDATE user_devices
                SET is_active = 0,
                    unbound_at_utc = @NowUtc
                WHERE user_id = @UserId
                  AND imei = @Imei
                  AND is_active = 1;
                """,
            DatabaseProvider.Postgres =>
                """
                UPDATE user_devices
                SET is_active = FALSE,
                    unbound_at_utc = @NowUtc
                WHERE user_id = @UserId
                  AND imei = @Imei
                  AND is_active = TRUE;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_update")
        };
    }

    private static string GetUsersOverviewSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP (@Limit)
                    up.user_id AS UserId,
                    up.email AS Email,
                    up.full_name AS FullName,
                    p.code AS PlanCode,
                    p.max_gps AS MaxGps,
                    CAST(ISNULL(usage_data.used_gps, 0) AS INT) AS UsedGps
                FROM user_profiles up
                INNER JOIN user_plan_subscriptions s
                    ON s.user_id = up.user_id
                   AND s.status = 'Active'
                   AND s.ends_at_utc IS NULL
                INNER JOIN plans p
                    ON p.plan_id = s.plan_id
                OUTER APPLY
                (
                    SELECT COUNT_BIG(*) AS used_gps
                    FROM user_devices ud
                    WHERE ud.user_id = up.user_id
                      AND ud.is_active = 1
                ) usage_data
                ORDER BY up.created_at_utc DESC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    up.user_id AS "UserId",
                    up.email AS "Email",
                    up.full_name AS "FullName",
                    p.code AS "PlanCode",
                    p.max_gps AS "MaxGps",
                    COALESCE(usage_data.used_gps, 0)::INT AS "UsedGps"
                FROM user_profiles up
                INNER JOIN user_plan_subscriptions s
                    ON s.user_id = up.user_id
                   AND s.status = 'Active'
                   AND s.ends_at_utc IS NULL
                INNER JOIN plans p
                    ON p.plan_id = s.plan_id
                LEFT JOIN LATERAL
                (
                    SELECT COUNT(*)::BIGINT AS used_gps
                    FROM user_devices ud
                    WHERE ud.user_id = up.user_id
                      AND ud.is_active = TRUE
                ) usage_data ON TRUE
                ORDER BY up.created_at_utc DESC
                LIMIT @Limit;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetPlanIdByCodeSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP 1 plan_id
                FROM plans
                WHERE code = @PlanCode
                  AND is_active = 1;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT plan_id
                FROM plans
                WHERE code = @PlanCode
                  AND is_active = TRUE
                LIMIT 1;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetDeactivateActiveSubscriptionsSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                UPDATE user_plan_subscriptions
                SET status = 'Ended',
                    ends_at_utc = @NowUtc
                WHERE user_id = @UserId
                  AND status = 'Active'
                  AND ends_at_utc IS NULL;
                """,
            DatabaseProvider.Postgres =>
                """
                UPDATE user_plan_subscriptions
                SET status = 'Ended',
                    ends_at_utc = @NowUtc
                WHERE user_id = @UserId
                  AND status = 'Active'
                  AND ends_at_utc IS NULL;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_update")
        };
    }

    private static string GetInsertSubscriptionSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                INSERT INTO user_plan_subscriptions
                (
                    subscription_id, user_id, plan_id, status, starts_at_utc, ends_at_utc, created_at_utc
                )
                VALUES
                (
                    @SubscriptionId, @UserId, @PlanId, 'Active', @StartsAtUtc, NULL, @CreatedAtUtc
                );
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO user_plan_subscriptions
                (
                    subscription_id, user_id, plan_id, status, starts_at_utc, ends_at_utc, created_at_utc
                )
                VALUES
                (
                    @SubscriptionId, @UserId, @PlanId, 'Active', @StartsAtUtc, NULL, @CreatedAtUtc
                );
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_insert")
        };
    }

    private static string GetInsertRawPacketSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF NOT EXISTS (SELECT 1 FROM raw_packets WHERE packet_id = @PacketId)
                BEGIN
                    INSERT INTO raw_packets
                    (
                        packet_id, session_id, device_id, imei, port, remote_ip, protocol, message_type,
                        payload_text, received_at_utc, parse_status, parse_error,
                        ack_sent, ack_payload, ack_at_utc, ack_latency_ms, queue_backlog
                    )
                    VALUES
                    (
                        @PacketId, @SessionId, @DeviceId, @Imei, @Port, @RemoteIp, @Protocol, @MessageType,
                        @PayloadText, @ReceivedAtUtc, @ParseStatus, @ParseError,
                        @AckSent, @AckPayload, @AckAtUtc, @AckLatencyMs, @QueueBacklog
                    );
                END;
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO raw_packets
                (
                    packet_id, session_id, device_id, imei, port, remote_ip, protocol, message_type,
                    payload_text, received_at_utc, parse_status, parse_error,
                    ack_sent, ack_payload, ack_at_utc, ack_latency_ms, queue_backlog
                )
                VALUES
                (
                    @PacketId, @SessionId, @DeviceId, @Imei, @Port, @RemoteIp, @Protocol, @MessageType,
                    @PayloadText, @ReceivedAtUtc, @ParseStatus, @ParseError,
                    @AckSent, @AckPayload, @AckAtUtc, @AckLatencyMs, @QueueBacklog
                )
                ON CONFLICT (packet_id) DO NOTHING;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_insert")
        };
    }

    private static string GetUpsertSessionSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF EXISTS (SELECT 1 FROM device_sessions WHERE session_id = @SessionId)
                BEGIN
                    UPDATE device_sessions
                    SET device_id = @DeviceId,
                        imei = @Imei,
                        remote_ip = @RemoteIp,
                        port = @Port,
                        connected_at_utc = @ConnectedAtUtc,
                        last_seen_at_utc = @LastSeenAtUtc,
                        last_heartbeat_at_utc = @LastHeartbeatAtUtc,
                        frames_in = @FramesIn,
                        frames_invalid = @FramesInvalid,
                        close_reason = @CloseReason,
                        disconnected_at_utc = @DisconnectedAtUtc,
                        is_active = @IsActive
                    WHERE session_id = @SessionId;
                END
                ELSE
                BEGIN
                    INSERT INTO device_sessions
                    (
                        session_id, device_id, imei, remote_ip, port,
                        connected_at_utc, last_seen_at_utc, last_heartbeat_at_utc,
                        frames_in, frames_invalid, close_reason, disconnected_at_utc, is_active
                    )
                    VALUES
                    (
                        @SessionId, @DeviceId, @Imei, @RemoteIp, @Port,
                        @ConnectedAtUtc, @LastSeenAtUtc, @LastHeartbeatAtUtc,
                        @FramesIn, @FramesInvalid, @CloseReason, @DisconnectedAtUtc, @IsActive
                    );
                END;
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO device_sessions
                (
                    session_id, device_id, imei, remote_ip, port,
                    connected_at_utc, last_seen_at_utc, last_heartbeat_at_utc,
                    frames_in, frames_invalid, close_reason, disconnected_at_utc, is_active
                )
                VALUES
                (
                    @SessionId, @DeviceId, @Imei, @RemoteIp, @Port,
                    @ConnectedAtUtc, @LastSeenAtUtc, @LastHeartbeatAtUtc,
                    @FramesIn, @FramesInvalid, @CloseReason, @DisconnectedAtUtc, @IsActive
                )
                ON CONFLICT (session_id)
                DO UPDATE SET
                    device_id = EXCLUDED.device_id,
                    imei = EXCLUDED.imei,
                    remote_ip = EXCLUDED.remote_ip,
                    port = EXCLUDED.port,
                    connected_at_utc = EXCLUDED.connected_at_utc,
                    last_seen_at_utc = EXCLUDED.last_seen_at_utc,
                    last_heartbeat_at_utc = EXCLUDED.last_heartbeat_at_utc,
                    frames_in = EXCLUDED.frames_in,
                    frames_invalid = EXCLUDED.frames_invalid,
                    close_reason = EXCLUDED.close_reason,
                    disconnected_at_utc = EXCLUDED.disconnected_at_utc,
                    is_active = EXCLUDED.is_active;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_upsert")
        };
    }

    private static string GetUpsertPortSnapshotSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF EXISTS (SELECT 1 FROM port_ingestion_snapshots WHERE port = @Port)
                BEGIN
                    UPDATE port_ingestion_snapshots
                    SET frames_in = frames_in + 1,
                        parse_ok = parse_ok + @ParseOkDelta,
                        parse_fail = parse_fail + @ParseFailDelta,
                        ack_sent = ack_sent + @AckDelta,
                        backlog = @QueueBacklog,
                        last_received_at_utc = @ReceivedAtUtc
                    WHERE port = @Port;
                END
                ELSE
                BEGIN
                    INSERT INTO port_ingestion_snapshots
                    (
                        port, frames_in, parse_ok, parse_fail, ack_sent, backlog, last_received_at_utc
                    )
                    VALUES
                    (
                        @Port, 1, @ParseOkDelta, @ParseFailDelta, @AckDelta, @QueueBacklog, @ReceivedAtUtc
                    );
                END;
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO port_ingestion_snapshots
                (
                    port, frames_in, parse_ok, parse_fail, ack_sent, backlog, last_received_at_utc
                )
                VALUES
                (
                    @Port, 1, @ParseOkDelta, @ParseFailDelta, @AckDelta, @QueueBacklog, @ReceivedAtUtc
                )
                ON CONFLICT (port)
                DO UPDATE SET
                    frames_in = port_ingestion_snapshots.frames_in + 1,
                    parse_ok = port_ingestion_snapshots.parse_ok + EXCLUDED.parse_ok,
                    parse_fail = port_ingestion_snapshots.parse_fail + EXCLUDED.parse_fail,
                    ack_sent = port_ingestion_snapshots.ack_sent + EXCLUDED.ack_sent,
                    backlog = EXCLUDED.backlog,
                    last_received_at_utc = EXCLUDED.last_received_at_utc;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_upsert")
        };
    }

    private sealed class RawPacketRow
    {
        public Guid PacketId { get; set; }

        public Guid SessionId { get; set; }

        public int Port { get; set; }

        public string RemoteIp { get; set; } = string.Empty;

        public int Protocol { get; set; }

        public string? Imei { get; set; }

        public int MessageType { get; set; }

        public string PayloadText { get; set; } = string.Empty;

        public DateTimeOffset ReceivedAtUtc { get; set; }

        public int ParseStatus { get; set; }

        public string? ParseError { get; set; }

        public bool AckSent { get; set; }

        public string? AckPayload { get; set; }

        public DateTimeOffset? AckAtUtc { get; set; }

        public double? AckLatencyMs { get; set; }
    }

    private sealed class ErrorAggregateRow
    {
        public string GroupKey { get; set; } = string.Empty;

        public long Count { get; set; }

        public Guid? LastPacketId { get; set; }
    }

    private sealed class SessionRow
    {
        public Guid SessionId { get; set; }

        public string RemoteIp { get; set; } = string.Empty;

        public int Port { get; set; }

        public DateTimeOffset ConnectedAtUtc { get; set; }

        public DateTimeOffset LastSeenAtUtc { get; set; }

        public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

        public string? Imei { get; set; }

        public long FramesIn { get; set; }

        public long FramesInvalid { get; set; }

        public string? CloseReason { get; set; }

        public DateTimeOffset? DisconnectedAtUtc { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class PortSnapshotRow
    {
        public int Port { get; set; }

        public int ActiveConnections { get; set; }

        public long FramesIn { get; set; }

        public long ParseOk { get; set; }

        public long ParseFail { get; set; }

        public long AckSent { get; set; }

        public long Backlog { get; set; }
    }

    private sealed class UserSummaryRow
    {
        public Guid UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public string PlanCode { get; set; } = string.Empty;

        public string PlanName { get; set; } = string.Empty;

        public int MaxGps { get; set; }

        public int UsedGps { get; set; }
    }

    private sealed class UserDeviceRow
    {
        public Guid DeviceId { get; set; }

        public string Imei { get; set; } = string.Empty;

        public DateTimeOffset BoundAtUtc { get; set; }
    }

    private sealed class DeviceOwnerRow
    {
        public Guid UserId { get; set; }

        public Guid DeviceId { get; set; }
    }

    private sealed class UserQuotaRow
    {
        public int MaxGps { get; set; }

        public int UsedGps { get; set; }
    }

    private sealed class UserOverviewRow
    {
        public Guid UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public string PlanCode { get; set; } = string.Empty;

        public int MaxGps { get; set; }

        public int UsedGps { get; set; }
    }
}
