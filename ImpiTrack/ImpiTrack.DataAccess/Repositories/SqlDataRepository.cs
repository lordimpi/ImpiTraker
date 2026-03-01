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
public sealed class SqlDataRepository : IOpsRepository, IIngestionRepository
{
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
                record.ReceivedAtUtc,
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
                session.LastSeenAtUtc,
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
                envelope.Message.ReceivedAtUtc,
                cancellationToken);

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
                        GpsTimeUtc = envelope.Message.ReceivedAtUtc,
                        Latitude = (decimal?)null,
                        Longitude = (decimal?)null,
                        SpeedKmh = (double?)null,
                        HeadingDeg = (int?)null,
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

    private async Task<Guid?> ResolveDeviceIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string? imei,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            return null;
        }

        CommandDefinition command = new(
            GetUpsertAndSelectDeviceSql(_context.Provider),
            new
            {
                DeviceId = Guid.NewGuid(),
                Imei = imei,
                SeenAtUtc = seenAtUtc
            },
            transaction,
            _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(command);
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

    private static string GetUpsertAndSelectDeviceSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF EXISTS (SELECT 1 FROM devices WHERE imei = @Imei)
                BEGIN
                    UPDATE devices
                    SET last_seen_at_utc = @SeenAtUtc
                    WHERE imei = @Imei;
                END
                ELSE
                BEGIN
                    INSERT INTO devices(device_id, imei, created_at_utc, last_seen_at_utc)
                    VALUES (@DeviceId, @Imei, @SeenAtUtc, @SeenAtUtc);
                END;

                SELECT device_id FROM devices WHERE imei = @Imei;
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO devices(device_id, imei, created_at_utc, last_seen_at_utc)
                VALUES (@DeviceId, @Imei, @SeenAtUtc, @SeenAtUtc)
                ON CONFLICT (imei)
                DO UPDATE SET last_seen_at_utc = EXCLUDED.last_seen_at_utc;

                SELECT device_id FROM devices WHERE imei = @Imei;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_upsert")
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
}
