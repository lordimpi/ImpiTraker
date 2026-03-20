using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Connection;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Queue;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace ImpiTrack.DataAccess.Repositories;

/// <summary>
/// Repositorio SQL con soporte para SQL Server y PostgreSQL.
/// </summary>
public sealed class SqlDataRepository : IOpsRepository, IIngestionRepository, IUserAccountRepository, ITelemetryQueryRepository
{
    private const string DefaultPlanCode = "BASIC";
    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50, 100];
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

            await UpsertRawPacketAsync(
                connection,
                transaction,
                record,
                deviceId,
                backlog,
                cancellationToken);

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
    public async Task<PersistEnvelopeResult> PersistEnvelopeAsync(InboundEnvelope envelope, CancellationToken cancellationToken)
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
                return new PersistEnvelopeResult(PersistEnvelopeStatus.SkippedUnownedDevice);
            }

            RawParseStatus rawParseStatus = envelope.Message.MessageType == MessageType.Tracking && !envelope.Message.IsTelemetryUsable
                ? RawParseStatus.Failed
                : RawParseStatus.Ok;
            string? rawParseError = rawParseStatus == RawParseStatus.Ok
                ? null
                : envelope.Message.TelemetryError ?? "invalid_tracking_payload";

            await UpsertRawPacketAsync(
                connection,
                transaction,
                new RawPacketRecord(
                    envelope.SessionId,
                    envelope.PacketId,
                    envelope.Port,
                    envelope.RemoteIp,
                    envelope.Message.Protocol,
                    envelope.Message.Imei,
                    envelope.Message.MessageType,
                    envelope.Message.Text,
                    envelope.Message.ReceivedAtUtc,
                    rawParseStatus,
                    rawParseError,
                    false,
                    null,
                    null,
                    null),
                deviceId,
                backlog: 0,
                cancellationToken);

            if (envelope.Message.MessageType == MessageType.Tracking)
            {
                if (!envelope.Message.IsTelemetryUsable)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new PersistEnvelopeResult(PersistEnvelopeStatus.Persisted);
                }

                string dedupeKey = BuildTrackingDedupeKey(envelope);
                CommandDefinition insertPosition = new(
                    GetInsertPositionSql(_context.Provider),
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
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        DedupeKey = dedupeKey
                    },
                    transaction,
                    _context.CommandTimeoutSeconds,
                    cancellationToken: cancellationToken);

                int inserted = await connection.ExecuteAsync(insertPosition);
                await transaction.CommitAsync(cancellationToken);
                if (inserted == 0)
                {
                    return new PersistEnvelopeResult(PersistEnvelopeStatus.Deduplicated);
                }

                return new PersistEnvelopeResult(PersistEnvelopeStatus.Persisted);
            }

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
                    ReceivedAtUtc = envelope.Message.ReceivedAtUtc,
                    OccurredAtUtc = envelope.Message.GpsTimeUtc ?? envelope.Message.ReceivedAtUtc
                },
                transaction,
                _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(insertEvent);

            await transaction.CommitAsync(cancellationToken);
            return new PersistEnvelopeResult(PersistEnvelopeStatus.Persisted);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new PersistEnvelopeResult(PersistEnvelopeStatus.Deduplicated);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task InsertDeviceIoEventAsync(DeviceIoEventRecord record, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        Guid? deviceId = null;
        if (!string.IsNullOrWhiteSpace(record.Imei))
        {
            // Resolve device_id without a transaction — best-effort lookup only.
            // If the device is not registered, we skip the insert silently.
            CommandDefinition resolveCommand = new(
                GetOwnedDeviceByImeiSql(_context.Provider),
                new { Imei = record.Imei },
                commandTimeout: _context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            deviceId = await connection.ExecuteScalarAsync<Guid?>(resolveCommand);
        }

        if (deviceId is null)
        {
            return;
        }

        CommandDefinition insertEvent = new(
            GetInsertEventSql(),
            new
            {
                EventId = Guid.NewGuid(),
                PacketId = record.PacketId.Value,
                SessionId = record.SessionId.Value,
                DeviceId = deviceId,
                Imei = record.Imei,
                Protocol = (int)record.Protocol,
                MessageType = (int)MessageType.Tracking,
                EventCode = record.EventCode,
                PayloadText = record.PayloadText,
                ReceivedAtUtc = record.ReceivedAtUtc,
                OccurredAtUtc = record.OccurredAtUtc
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(insertEvent);
    }

    /// <inheritdoc />
    public async Task<PagedResult<RawPacketRecord>> GetLatestRawPacketsAsync(
        OpsRawListQuery query,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int offset = (page - 1) * pageSize;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var parameters = new { Imei = query.Imei, Offset = offset, PageSize = pageSize };

        CommandDefinition countCommand = new(
            GetRawPacketsCountSql(_context.Provider),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        long totalItems = await connection.QuerySingleAsync<long>(countCommand);

        CommandDefinition command = new(
            GetRawPacketsPageSql(_context.Provider),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<RawPacketRow> rows = await connection.QueryAsync<RawPacketRow>(command);
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<RawPacketRecord>(
            rows.Select(ToRawPacketRecord).ToArray(),
            page,
            pageSize,
            (int)totalItems,
            totalPages);
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
    public async Task<PagedResult<ErrorAggregate>> GetTopErrorsAsync(OpsErrorListQuery query, CancellationToken cancellationToken)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = AllowedPageSizes.Contains(query.PageSize) ? query.PageSize : 20;
        int offset = (page - 1) * pageSize;

        DateTimeOffset toUtc = query.To ?? DateTimeOffset.UtcNow;
        DateTimeOffset fromUtc = query.From ?? toUtc.AddHours(-1);

        string grouping = NormalizeGrouping(query.GroupBy);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetTopErrorsSql(grouping, _context.Provider),
            new { FromUtc = fromUtc, ToUtc = toUtc, Offset = offset, PageSize = pageSize },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<ErrorAggregateRow> rows = await connection.QueryAsync<ErrorAggregateRow>(command);
        ErrorAggregateRow[] materialized = rows.ToArray();

        int totalItems = materialized.Length > 0 ? materialized[0].TotalCount : 0;
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        ErrorAggregate[] items = materialized
            .Select(x => new ErrorAggregate(x.GroupKey, x.Count, x.LastPacketId.HasValue ? new PacketId(x.LastPacketId.Value) : null))
            .ToArray();

        return new PagedResult<ErrorAggregate>(items, page, pageSize, totalItems, totalPages);
    }

    /// <inheritdoc />
    public async Task<PagedResult<SessionRecord>> GetActiveSessionsAsync(OpsSessionListQuery query, CancellationToken cancellationToken)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int offset = (page - 1) * pageSize;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var parameters = new { Port = query.Port, Offset = offset, PageSize = pageSize };

        CommandDefinition countCommand = new(
            GetSessionsCountSql(_context.Provider),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int totalItems = await connection.QuerySingleAsync<int>(countCommand);

        CommandDefinition command = new(
            GetSessionsPageSql(_context.Provider),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<SessionRow> rows = await connection.QueryAsync<SessionRow>(command);
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<SessionRecord>(
            rows.Select(ToSessionRecord).ToArray(),
            page,
            pageSize,
            totalItems,
            totalPages);
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
            GetUserDevicesAllSql(_context.Provider),
            new { UserId = userId },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<UserDeviceRow> rows = await connection.QueryAsync<UserDeviceRow>(command);
        return rows.Select(x => new UserDeviceBinding(x.DeviceId, x.Imei, x.BoundAtUtc, x.Alias)).ToArray();
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserDeviceBinding>> GetUserDevicesPagedAsync(Guid userId, AdminDeviceListQuery query, CancellationToken cancellationToken)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int offset = (page - 1) * pageSize;
        string sortDirection = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var parameters = new { UserId = userId, Offset = offset, PageSize = pageSize, Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim() };

        CommandDefinition countCommand = new(
            GetUserDevicesCountSql(_context.Provider),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int totalItems = await connection.QuerySingleAsync<int>(countCommand);

        CommandDefinition command = new(
            GetUserDevicesPageSql(
                _context.Provider,
                ResolveDeviceSortColumn(_context.Provider, query.SortBy.Trim()),
                sortDirection),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<UserDeviceRow> rows = await connection.QueryAsync<UserDeviceRow>(command);
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<UserDeviceBinding>(
            rows.Select(x => new UserDeviceBinding(x.DeviceId, x.Imei, x.BoundAtUtc, x.Alias)).ToArray(),
            page,
            pageSize,
            totalItems,
            totalPages);
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserDeviceBinding>> GetUserDevicesPagedMeAsync(Guid userId, MeDeviceListQuery query, CancellationToken cancellationToken)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int offset = (page - 1) * pageSize;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var parameters = new { UserId = userId, Offset = offset, PageSize = pageSize, Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim() };

        CommandDefinition countCommand = new(
            GetUserDevicesCountSql(_context.Provider),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int totalItems = await connection.QuerySingleAsync<int>(countCommand);

        CommandDefinition command = new(
            GetUserDevicesPagedMeSql(_context.Provider),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<UserDeviceRow> rows = await connection.QueryAsync<UserDeviceRow>(command);
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<UserDeviceBinding>(
            rows.Select(x => new UserDeviceBinding(x.DeviceId, x.Imei, x.BoundAtUtc, x.Alias)).ToArray(),
            page,
            pageSize,
            totalItems,
            totalPages);
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
    public async Task<PagedResult<UserAccountOverview>> GetUsersAsync(AdminUserListQuery query, CancellationToken cancellationToken)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int offset = (page - 1) * pageSize;
        string sortBy = query.SortBy.Trim();
        string sortDirection = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
        string? search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        string? planCode = string.IsNullOrWhiteSpace(query.PlanCode) ? null : query.PlanCode.Trim().ToUpperInvariant();

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var parameters = new
        {
            Search = search,
            SearchPattern = search is null ? null : $"%{search}%",
            PlanCode = planCode,
            Offset = offset,
            PageSize = pageSize
        };

        CommandDefinition countCommand = new(
            GetUsersCountSql(_context.Provider),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int totalItems = await connection.QuerySingleAsync<int>(countCommand);

        CommandDefinition command = new(
            GetUsersOverviewPageSql(
                _context.Provider,
                ResolveUserSortColumn(_context.Provider, sortBy),
                sortDirection),
            parameters,
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<UserOverviewRow> rows = await connection.QueryAsync<UserOverviewRow>(command);
        IReadOnlyList<UserAccountOverview> items = rows.Select(x => new UserAccountOverview(
            x.UserId,
            x.Email,
            x.FullName,
            x.PlanCode,
            x.MaxGps,
            x.UsedGps)).ToArray();

        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<UserAccountOverview>(items, page, pageSize, totalItems, totalPages);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetPlansSql(_context.Provider),
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<PlanRow> rows = await connection.QueryAsync<PlanRow>(command);
        return rows.Select(x => new AdminPlanDto(
            x.PlanId,
            x.Code,
            x.Name,
            x.MaxGps,
            x.IsActive)).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TelemetryDeviceSummaryDto>> GetDeviceSummariesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetTelemetryDeviceSummariesSql(_context.Provider),
            new { UserId = userId },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<TelemetryDeviceSummaryRow> rows = await connection.QueryAsync<TelemetryDeviceSummaryRow>(command);
        return rows.Select(ToTelemetryDeviceSummary).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveDeviceBindingAsync(Guid userId, string imei, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetHasActiveDeviceBindingSql(_context.Provider),
            new
            {
                UserId = userId,
                Imei = imei.Trim()
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int? result = await connection.ExecuteScalarAsync<int?>(command);
        return result.HasValue;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DevicePositionPointDto>> GetPositionsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetTelemetryPositionsSql(_context.Provider),
            new
            {
                UserId = userId,
                Imei = imei.Trim(),
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Limit = limit
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<TelemetryPositionRow> rows = await connection.QueryAsync<TelemetryPositionRow>(command);
        return rows.Select(ToDevicePositionPoint).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceEventDto>> GetEventsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetTelemetryEventsSql(_context.Provider),
            new
            {
                UserId = userId,
                Imei = imei.Trim(),
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Limit = limit
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<TelemetryEventRow> rows = await connection.QueryAsync<TelemetryEventRow>(command);
        return rows.Select(ToDeviceEvent).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DevicePositionPointDto>> GetTripCandidatePositionsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetTelemetryTripCandidatesSql(_context.Provider),
            new
            {
                UserId = userId,
                Imei = imei.Trim(),
                FromUtc = fromUtc,
                ToUtc = toUtc,
                MaxPoints = maxPoints
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<TelemetryPositionRow> rows = await connection.QueryAsync<TelemetryPositionRow>(command);
        return rows.Select(ToDevicePositionPoint).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccEventDto>> GetAccEventsForWindowAsync(
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetAccEventsForWindowSql(_context.Provider),
            new
            {
                Imei = imei.Trim(),
                FromUtc = fromUtc,
                ToUtc = toUtc
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<AccEventRow> rows = await connection.QueryAsync<AccEventRow>(command);
        return rows
            .Select(x => new AccEventDto(x.OccurredAtUtc, string.Equals(x.EventCode, "ACC_ON", StringComparison.Ordinal)))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetActiveUserIdsByImeiAsync(string imei, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetActiveUserIdsByImeiSql(_context.Provider),
            new { Imei = imei.Trim() },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<Guid> userIds = await connection.QueryAsync<Guid>(command);
        return userIds.ToArray();
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

    /// <inheritdoc />
    public async Task<bool> UpdateDeviceAliasAsync(
        Guid userId,
        string imei,
        string? alias,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(
            GetUpdateDeviceAliasSql(_context.Provider),
            new
            {
                UserId = userId,
                Imei = imei.Trim(),
                Alias = alias
            },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int affected = await connection.ExecuteAsync(command);
        return affected > 0;
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

    private static TelemetryDeviceSummaryDto ToTelemetryDeviceSummary(TelemetryDeviceSummaryRow row)
    {
        LastKnownPositionDto? lastPosition = row.LastPositionPacketId.HasValue &&
                                             row.LastPositionSessionId.HasValue &&
                                             row.LastPositionLatitude.HasValue &&
                                             row.LastPositionLongitude.HasValue &&
                                             row.LastPositionOccurredAtUtc.HasValue &&
                                             row.LastPositionReceivedAtUtc.HasValue
            ? new LastKnownPositionDto(
                row.LastPositionOccurredAtUtc.Value,
                row.LastPositionReceivedAtUtc.Value,
                row.LastPositionGpsTimeUtc,
                row.LastPositionLatitude.Value,
                row.LastPositionLongitude.Value,
                row.LastPositionSpeedKmh,
                row.LastPositionHeadingDeg,
                row.LastPositionPacketId.Value,
                row.LastPositionSessionId.Value)
            : null;

        return new TelemetryDeviceSummaryDto(
            row.Imei,
            row.BoundAtUtc,
            row.LastSeenAtUtc,
            row.ActiveSessionId,
            ToNullableProtocolId(row.Protocol),
            ToNullableMessageType(row.LastMessageType),
            lastPosition,
            row.Alias);
    }

    private static DevicePositionPointDto ToDevicePositionPoint(TelemetryPositionRow row)
    {
        return new DevicePositionPointDto(
            row.OccurredAtUtc,
            row.ReceivedAtUtc,
            row.GpsTimeUtc,
            row.Latitude,
            row.Longitude,
            row.SpeedKmh,
            row.HeadingDeg,
            row.PacketId,
            row.SessionId);
    }

    private static DeviceEventDto ToDeviceEvent(TelemetryEventRow row)
    {
        return new DeviceEventDto(
            row.EventId,
            row.OccurredAtUtc,
            row.ReceivedAtUtc,
            row.EventCode,
            row.PayloadText,
            ToProtocolId(row.Protocol),
            ToMessageType(row.MessageType),
            row.PacketId,
            row.SessionId);
    }

    private static ProtocolId ToProtocolId(int value)
    {
        return Enum.IsDefined(typeof(ProtocolId), value)
            ? (ProtocolId)value
            : ProtocolId.Unknown;
    }

    private static ProtocolId? ToNullableProtocolId(int? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return Enum.IsDefined(typeof(ProtocolId), value.Value)
            ? (ProtocolId)value.Value
            : null;
    }

    private static MessageType ToMessageType(int value)
    {
        return Enum.IsDefined(typeof(MessageType), value)
            ? (MessageType)value
            : MessageType.Unknown;
    }

    private static MessageType? ToNullableMessageType(int? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return Enum.IsDefined(typeof(MessageType), value.Value)
            ? (MessageType)value.Value
            : null;
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

    private static string BuildTrackingDedupeKey(InboundEnvelope envelope)
    {
        string imei = envelope.Message.Imei?.Trim() ?? string.Empty;
        string gpsTime = (envelope.Message.GpsTimeUtc ?? envelope.Message.ReceivedAtUtc)
            .UtcDateTime
            .ToString("O", CultureInfo.InvariantCulture);
        string latitude = envelope.Message.Latitude?.ToString("F6", CultureInfo.InvariantCulture) ?? "na";
        string longitude = envelope.Message.Longitude?.ToString("F6", CultureInfo.InvariantCulture) ?? "na";
        string source = string.Join(
            "|",
            imei,
            gpsTime,
            envelope.Message.MessageType.ToString(),
            latitude,
            longitude);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash);
    }

    private async Task UpsertRawPacketAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RawPacketRecord record,
        Guid? deviceId,
        long backlog,
        CancellationToken cancellationToken)
    {
        CommandDefinition upsertRaw = new(
            GetUpsertRawPacketSql(_context.Provider),
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

        await connection.ExecuteAsync(upsertRaw);
    }

    private static bool IsDuplicateKey(Exception exception)
    {
        if (exception is SqlException sqlException)
        {
            return sqlException.Number is 2601 or 2627;
        }

        if (exception is PostgresException postgresException)
        {
            return string.Equals(postgresException.SqlState, "23505", StringComparison.Ordinal);
        }

        if (exception.InnerException is not null)
        {
            return IsDuplicateKey(exception.InnerException);
        }

        return false;
    }

    private static string GetInsertPositionSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF NOT EXISTS (SELECT 1 FROM positions WHERE dedupe_key = @DedupeKey)
                BEGIN
                    INSERT INTO positions
                    (
                        position_id, packet_id, session_id, device_id, imei, protocol, message_type,
                        gps_time_utc, latitude, longitude, speed_kmh, heading_deg, created_at_utc, dedupe_key
                    )
                    VALUES
                    (
                        @PositionId, @PacketId, @SessionId, @DeviceId, @Imei, @Protocol, @MessageType,
                        @GpsTimeUtc, @Latitude, @Longitude, @SpeedKmh, @HeadingDeg, @CreatedAtUtc, @DedupeKey
                    );
                END;
                """,
            DatabaseProvider.Postgres =>
                """
                INSERT INTO positions
                (
                    position_id, packet_id, session_id, device_id, imei, protocol, message_type,
                    gps_time_utc, latitude, longitude, speed_kmh, heading_deg, created_at_utc, dedupe_key
                )
                VALUES
                (
                    @PositionId, @PacketId, @SessionId, @DeviceId, @Imei, @Protocol, @MessageType,
                    @GpsTimeUtc, @Latitude, @Longitude, @SpeedKmh, @HeadingDeg, @CreatedAtUtc, @DedupeKey
                )
                ON CONFLICT (dedupe_key) DO NOTHING;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_insert")
        };
    }

    private static string GetInsertEventSql()
    {
        return
            """
            INSERT INTO device_events
            (
                event_id, packet_id, session_id, device_id, imei, protocol, message_type,
                event_code, payload_text, received_at_utc, occurred_at_utc
            )
            VALUES
            (
                @EventId, @PacketId, @SessionId, @DeviceId, @Imei, @Protocol, @MessageType,
                @EventCode, @PayloadText, @ReceivedAtUtc, @OccurredAtUtc
            );
            """;
    }

    private static string GetRawPacketsCountSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT COUNT_BIG(*)
                FROM raw_packets
                WHERE (@Imei IS NULL OR imei = @Imei);
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT COUNT(*)::BIGINT
                FROM raw_packets
                WHERE (@Imei IS NULL OR imei = @Imei);
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetRawPacketsPageSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
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
                WHERE (@Imei IS NULL OR imei = @Imei)
                ORDER BY received_at_utc DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
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
                LIMIT @PageSize OFFSET @Offset;
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
                WITH aggregated AS (
                    SELECT
                        {groupExpression} AS group_key,
                        COUNT_BIG(*) AS count,
                        MAX(packet_id) AS last_packet_id
                    FROM raw_packets
                    WHERE received_at_utc >= @FromUtc AND received_at_utc < @ToUtc
                      AND parse_status <> 1
                    GROUP BY {groupExpression}
                )
                SELECT
                    group_key AS GroupKey,
                    count AS Count,
                    last_packet_id AS LastPacketId,
                    COUNT(*) OVER() AS TotalCount
                FROM aggregated
                ORDER BY count DESC, group_key ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
            DatabaseProvider.Postgres =>
                $"""
                WITH aggregated AS (
                    SELECT
                        {groupExpression} AS group_key,
                        COUNT(*)::BIGINT AS count,
                        MAX(packet_id) AS last_packet_id
                    FROM raw_packets
                    WHERE received_at_utc >= @FromUtc AND received_at_utc < @ToUtc
                      AND parse_status <> 1
                    GROUP BY {groupExpression}
                )
                SELECT
                    group_key AS "GroupKey",
                    count AS "Count",
                    last_packet_id AS "LastPacketId",
                    COUNT(*) OVER() AS "TotalCount"
                FROM aggregated
                ORDER BY count DESC, group_key ASC
                LIMIT @PageSize OFFSET @Offset;
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

    private static string GetSessionsCountSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT COUNT(*)
                FROM device_sessions
                WHERE is_active = 1
                  AND (@Port IS NULL OR port = @Port);
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT COUNT(*)::INT
                FROM device_sessions
                WHERE is_active = TRUE
                  AND (@Port IS NULL OR port = @Port);
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetSessionsPageSql(DatabaseProvider provider)
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
                ORDER BY last_seen_at_utc DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
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
                ORDER BY last_seen_at_utc DESC
                LIMIT @PageSize OFFSET @Offset;
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

    private static string ResolveDeviceSortColumn(DatabaseProvider provider, string sortBy)
    {
        string normalized = sortBy.Trim().ToLowerInvariant();
        return provider switch
        {
            DatabaseProvider.SqlServer => normalized switch
            {
                "imei" => "ud.imei",
                "boundatutc" => "ud.bound_at_utc",
                "alias" => "ISNULL(ud.alias, '')",
                _ => "ud.bound_at_utc"
            },
            DatabaseProvider.Postgres => normalized switch
            {
                "imei" => "ud.imei",
                "boundatutc" => "ud.bound_at_utc",
                "alias" => "COALESCE(ud.alias, '')",
                _ => "ud.bound_at_utc"
            },
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetUserDevicesAllSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    ud.device_id AS DeviceId,
                    ud.imei AS Imei,
                    ud.bound_at_utc AS BoundAtUtc,
                    ud.alias AS Alias
                FROM user_devices ud
                WHERE ud.user_id = @UserId
                  AND ud.is_active = 1
                ORDER BY ud.bound_at_utc DESC, ud.imei ASC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    ud.device_id AS "DeviceId",
                    ud.imei AS "Imei",
                    ud.bound_at_utc AS "BoundAtUtc",
                    ud.alias AS "Alias"
                FROM user_devices ud
                WHERE ud.user_id = @UserId
                  AND ud.is_active = TRUE
                ORDER BY ud.bound_at_utc DESC, ud.imei ASC;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetUserDevicesCountSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT COUNT(*)
                FROM user_devices ud
                WHERE ud.user_id = @UserId
                  AND ud.is_active = 1
                  AND (@Search IS NULL OR ud.imei LIKE '%' + @Search + '%' OR ud.alias LIKE '%' + @Search + '%');
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT COUNT(*)::INT
                FROM user_devices ud
                WHERE ud.user_id = @UserId
                  AND ud.is_active = TRUE
                  AND (@Search IS NULL OR ud.imei ILIKE '%' || @Search || '%' OR ud.alias ILIKE '%' || @Search || '%');
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetUserDevicesPageSql(DatabaseProvider provider, string orderByColumn, string sortDirection)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                $"""
                SELECT
                    ud.device_id AS DeviceId,
                    ud.imei AS Imei,
                    ud.bound_at_utc AS BoundAtUtc,
                    ud.alias AS Alias
                FROM user_devices ud
                WHERE ud.user_id = @UserId
                  AND ud.is_active = 1
                  AND (@Search IS NULL OR ud.imei LIKE '%' + @Search + '%' OR ud.alias LIKE '%' + @Search + '%')
                ORDER BY {orderByColumn} {sortDirection}, ud.imei ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
            DatabaseProvider.Postgres =>
                $"""
                SELECT
                    ud.device_id AS "DeviceId",
                    ud.imei AS "Imei",
                    ud.bound_at_utc AS "BoundAtUtc",
                    ud.alias AS "Alias"
                FROM user_devices ud
                WHERE ud.user_id = @UserId
                  AND ud.is_active = TRUE
                  AND (@Search IS NULL OR ud.imei ILIKE '%' || @Search || '%' OR ud.alias ILIKE '%' || @Search || '%')
                ORDER BY {orderByColumn} {sortDirection}, ud.imei ASC
                LIMIT @PageSize OFFSET @Offset;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetUserDevicesPagedMeSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    ud.device_id AS DeviceId,
                    ud.imei AS Imei,
                    ud.bound_at_utc AS BoundAtUtc,
                    ud.alias AS Alias
                FROM user_devices ud
                WHERE ud.user_id = @UserId
                  AND ud.is_active = 1
                  AND (@Search IS NULL OR ud.imei LIKE '%' + @Search + '%' OR ud.alias LIKE '%' + @Search + '%')
                ORDER BY ud.bound_at_utc DESC, ud.imei ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    ud.device_id AS "DeviceId",
                    ud.imei AS "Imei",
                    ud.bound_at_utc AS "BoundAtUtc",
                    ud.alias AS "Alias"
                FROM user_devices ud
                WHERE ud.user_id = @UserId
                  AND ud.is_active = TRUE
                  AND (@Search IS NULL OR ud.imei ILIKE '%' || @Search || '%' OR ud.alias ILIKE '%' || @Search || '%')
                ORDER BY ud.bound_at_utc DESC, ud.imei ASC
                LIMIT @PageSize OFFSET @Offset;
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

    private static string GetUpdateDeviceAliasSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                UPDATE user_devices
                SET alias = @Alias
                WHERE user_id = @UserId
                  AND imei = @Imei
                  AND is_active = 1;
                """,
            DatabaseProvider.Postgres =>
                """
                UPDATE user_devices
                SET alias = @Alias
                WHERE user_id = @UserId
                  AND imei = @Imei
                  AND is_active = TRUE;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_update")
        };
    }

    private static string GetUsersCountSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT COUNT(*)
                FROM user_profiles up
                INNER JOIN user_plan_subscriptions s
                    ON s.user_id = up.user_id
                   AND s.status = 'Active'
                   AND s.ends_at_utc IS NULL
                INNER JOIN plans p
                    ON p.plan_id = s.plan_id
                WHERE (@Search IS NULL
                    OR up.email LIKE @SearchPattern
                    OR ISNULL(up.full_name, '') LIKE @SearchPattern)
                  AND (@PlanCode IS NULL OR p.code = @PlanCode);
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT COUNT(*)::INT
                FROM user_profiles up
                INNER JOIN user_plan_subscriptions s
                    ON s.user_id = up.user_id
                   AND s.status = 'Active'
                   AND s.ends_at_utc IS NULL
                INNER JOIN plans p
                    ON p.plan_id = s.plan_id
                WHERE (@Search IS NULL
                    OR up.email ILIKE @SearchPattern
                    OR COALESCE(up.full_name, '') ILIKE @SearchPattern)
                  AND (@PlanCode IS NULL OR p.code = @PlanCode);
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetUsersOverviewPageSql(DatabaseProvider provider, string orderByColumn, string sortDirection)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                $"""
                SELECT
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
                WHERE (@Search IS NULL
                    OR up.email LIKE @SearchPattern
                    OR ISNULL(up.full_name, '') LIKE @SearchPattern)
                  AND (@PlanCode IS NULL OR p.code = @PlanCode)
                ORDER BY {orderByColumn} {sortDirection}, up.user_id ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
            DatabaseProvider.Postgres =>
                $"""
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
                WHERE (@Search IS NULL
                    OR up.email ILIKE @SearchPattern
                    OR COALESCE(up.full_name, '') ILIKE @SearchPattern)
                  AND (@PlanCode IS NULL OR p.code = @PlanCode)
                ORDER BY {orderByColumn} {sortDirection}, up.user_id ASC
                LIMIT @PageSize OFFSET @Offset;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string ResolveUserSortColumn(DatabaseProvider provider, string sortBy)
    {
        string normalized = sortBy.Trim().ToLowerInvariant();
        return provider switch
        {
            DatabaseProvider.SqlServer => normalized switch
            {
                "email" => "up.email",
                "fullname" => "ISNULL(up.full_name, '')",
                "plancode" => "p.code",
                "maxgps" => "p.max_gps",
                "usedgps" => "ISNULL(usage_data.used_gps, 0)",
                "createdat" => "up.created_at_utc",
                _ => "up.email"
            },
            DatabaseProvider.Postgres => normalized switch
            {
                "email" => "up.email",
                "fullname" => "COALESCE(up.full_name, '')",
                "plancode" => "p.code",
                "maxgps" => "p.max_gps",
                "usedgps" => "COALESCE(usage_data.used_gps, 0)",
                "createdat" => "up.created_at_utc",
                _ => "up.email"
            },
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetPlansSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    plan_id AS PlanId,
                    code AS Code,
                    name AS Name,
                    max_gps AS MaxGps,
                    is_active AS IsActive
                FROM plans
                WHERE is_active = 1
                ORDER BY name ASC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    plan_id AS "PlanId",
                    code AS "Code",
                    name AS "Name",
                    max_gps AS "MaxGps",
                    is_active AS "IsActive"
                FROM plans
                WHERE is_active = TRUE
                ORDER BY name ASC;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetTelemetryDeviceSummariesSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    ud.imei AS Imei,
                    ud.bound_at_utc AS BoundAtUtc,
                    COALESCE(active_session.LastSeenAtUtc, latest_raw.ReceivedAtUtc, latest_position.ReceivedAtUtc, latest_event.ReceivedAtUtc) AS LastSeenAtUtc,
                    active_session.SessionId AS ActiveSessionId,
                    latest_raw.Protocol AS Protocol,
                    latest_raw.MessageType AS LastMessageType,
                    latest_position.OccurredAtUtc AS LastPositionOccurredAtUtc,
                    latest_position.ReceivedAtUtc AS LastPositionReceivedAtUtc,
                    latest_position.GpsTimeUtc AS LastPositionGpsTimeUtc,
                    latest_position.Latitude AS LastPositionLatitude,
                    latest_position.Longitude AS LastPositionLongitude,
                    latest_position.SpeedKmh AS LastPositionSpeedKmh,
                    latest_position.HeadingDeg AS LastPositionHeadingDeg,
                    latest_position.PacketId AS LastPositionPacketId,
                    latest_position.SessionId AS LastPositionSessionId,
                    ud.alias AS Alias
                FROM user_devices ud
                OUTER APPLY
                (
                    SELECT TOP 1
                        session_id AS SessionId,
                        last_seen_at_utc AS LastSeenAtUtc
                    FROM device_sessions
                    WHERE imei = ud.imei
                      AND is_active = 1
                    ORDER BY last_seen_at_utc DESC, session_id ASC
                ) active_session
                OUTER APPLY
                (
                    SELECT TOP 1
                        protocol AS Protocol,
                        message_type AS MessageType,
                        received_at_utc AS ReceivedAtUtc
                    FROM raw_packets
                    WHERE imei = ud.imei
                    ORDER BY received_at_utc DESC, packet_id DESC
                ) latest_raw
                OUTER APPLY
                (
                    SELECT TOP 1
                        p.gps_time_utc AS OccurredAtUtc,
                        rp.received_at_utc AS ReceivedAtUtc,
                        p.gps_time_utc AS GpsTimeUtc,
                        CAST(p.latitude AS FLOAT) AS Latitude,
                        CAST(p.longitude AS FLOAT) AS Longitude,
                        p.speed_kmh AS SpeedKmh,
                        p.heading_deg AS HeadingDeg,
                        p.packet_id AS PacketId,
                        p.session_id AS SessionId
                    FROM positions p
                    INNER JOIN raw_packets rp
                        ON rp.packet_id = p.packet_id
                    WHERE p.imei = ud.imei
                      AND p.latitude IS NOT NULL
                      AND p.longitude IS NOT NULL
                    ORDER BY p.gps_time_utc DESC, rp.received_at_utc DESC, p.position_id DESC
                ) latest_position
                OUTER APPLY
                (
                    SELECT TOP 1
                        received_at_utc AS ReceivedAtUtc
                    FROM device_events
                    WHERE imei = ud.imei
                    ORDER BY received_at_utc DESC, event_id DESC
                ) latest_event
                WHERE ud.user_id = @UserId
                  AND ud.is_active = 1
                ORDER BY ud.bound_at_utc DESC, ud.imei ASC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    ud.imei AS "Imei",
                    ud.bound_at_utc AS "BoundAtUtc",
                    COALESCE(active_session."LastSeenAtUtc", latest_raw."ReceivedAtUtc", latest_position."ReceivedAtUtc", latest_event."ReceivedAtUtc") AS "LastSeenAtUtc",
                    active_session."SessionId" AS "ActiveSessionId",
                    latest_raw."Protocol" AS "Protocol",
                    latest_raw."MessageType" AS "LastMessageType",
                    latest_position."OccurredAtUtc" AS "LastPositionOccurredAtUtc",
                    latest_position."ReceivedAtUtc" AS "LastPositionReceivedAtUtc",
                    latest_position."GpsTimeUtc" AS "LastPositionGpsTimeUtc",
                    latest_position."Latitude" AS "LastPositionLatitude",
                    latest_position."Longitude" AS "LastPositionLongitude",
                    latest_position."SpeedKmh" AS "LastPositionSpeedKmh",
                    latest_position."HeadingDeg" AS "LastPositionHeadingDeg",
                    latest_position."PacketId" AS "LastPositionPacketId",
                    latest_position."SessionId" AS "LastPositionSessionId",
                    ud.alias AS "Alias"
                FROM user_devices ud
                LEFT JOIN LATERAL
                (
                    SELECT
                        session_id AS "SessionId",
                        last_seen_at_utc AS "LastSeenAtUtc"
                    FROM device_sessions
                    WHERE imei = ud.imei
                      AND is_active = TRUE
                    ORDER BY last_seen_at_utc DESC, session_id ASC
                    LIMIT 1
                ) active_session ON TRUE
                LEFT JOIN LATERAL
                (
                    SELECT
                        protocol AS "Protocol",
                        message_type AS "MessageType",
                        received_at_utc AS "ReceivedAtUtc"
                    FROM raw_packets
                    WHERE imei = ud.imei
                    ORDER BY received_at_utc DESC, packet_id DESC
                    LIMIT 1
                ) latest_raw ON TRUE
                LEFT JOIN LATERAL
                (
                    SELECT
                        p.gps_time_utc AS "OccurredAtUtc",
                        rp.received_at_utc AS "ReceivedAtUtc",
                        p.gps_time_utc AS "GpsTimeUtc",
                        p.latitude::DOUBLE PRECISION AS "Latitude",
                        p.longitude::DOUBLE PRECISION AS "Longitude",
                        p.speed_kmh AS "SpeedKmh",
                        p.heading_deg AS "HeadingDeg",
                        p.packet_id AS "PacketId",
                        p.session_id AS "SessionId"
                    FROM positions p
                    INNER JOIN raw_packets rp
                        ON rp.packet_id = p.packet_id
                    WHERE p.imei = ud.imei
                      AND p.latitude IS NOT NULL
                      AND p.longitude IS NOT NULL
                    ORDER BY p.gps_time_utc DESC, rp.received_at_utc DESC, p.position_id DESC
                    LIMIT 1
                ) latest_position ON TRUE
                LEFT JOIN LATERAL
                (
                    SELECT
                        received_at_utc AS "ReceivedAtUtc"
                    FROM device_events
                    WHERE imei = ud.imei
                    ORDER BY received_at_utc DESC, event_id DESC
                    LIMIT 1
                ) latest_event ON TRUE
                WHERE ud.user_id = @UserId
                  AND ud.is_active = TRUE
                ORDER BY ud.bound_at_utc DESC, ud.imei ASC;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetHasActiveDeviceBindingSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP 1 1
                FROM user_devices
                WHERE user_id = @UserId
                  AND imei = @Imei
                  AND is_active = 1;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT 1
                FROM user_devices
                WHERE user_id = @UserId
                  AND imei = @Imei
                  AND is_active = TRUE
                LIMIT 1;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetTelemetryPositionsSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP (@Limit)
                    p.gps_time_utc AS OccurredAtUtc,
                    rp.received_at_utc AS ReceivedAtUtc,
                    p.gps_time_utc AS GpsTimeUtc,
                    CAST(p.latitude AS FLOAT) AS Latitude,
                    CAST(p.longitude AS FLOAT) AS Longitude,
                    p.speed_kmh AS SpeedKmh,
                    p.heading_deg AS HeadingDeg,
                    p.packet_id AS PacketId,
                    p.session_id AS SessionId
                FROM positions p
                INNER JOIN raw_packets rp
                    ON rp.packet_id = p.packet_id
                INNER JOIN user_devices ud
                    ON ud.user_id = @UserId
                   AND ud.imei = @Imei
                   AND ud.is_active = 1
                   AND ud.imei = p.imei
                WHERE p.gps_time_utc >= @FromUtc
                  AND p.gps_time_utc <= @ToUtc
                  AND p.latitude IS NOT NULL
                  AND p.longitude IS NOT NULL
                ORDER BY p.gps_time_utc DESC, rp.received_at_utc DESC, p.position_id DESC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    p.gps_time_utc AS "OccurredAtUtc",
                    rp.received_at_utc AS "ReceivedAtUtc",
                    p.gps_time_utc AS "GpsTimeUtc",
                    p.latitude::DOUBLE PRECISION AS "Latitude",
                    p.longitude::DOUBLE PRECISION AS "Longitude",
                    p.speed_kmh AS "SpeedKmh",
                    p.heading_deg AS "HeadingDeg",
                    p.packet_id AS "PacketId",
                    p.session_id AS "SessionId"
                FROM positions p
                INNER JOIN raw_packets rp
                    ON rp.packet_id = p.packet_id
                INNER JOIN user_devices ud
                    ON ud.user_id = @UserId
                   AND ud.imei = @Imei
                   AND ud.is_active = TRUE
                   AND ud.imei = p.imei
                WHERE p.gps_time_utc >= @FromUtc
                  AND p.gps_time_utc <= @ToUtc
                  AND p.latitude IS NOT NULL
                  AND p.longitude IS NOT NULL
                ORDER BY p.gps_time_utc DESC, rp.received_at_utc DESC, p.position_id DESC
                LIMIT @Limit;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetTelemetryEventsSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP (@Limit)
                    ev.event_id AS EventId,
                    COALESCE(ev.occurred_at_utc, ev.received_at_utc) AS OccurredAtUtc,
                    ev.received_at_utc AS ReceivedAtUtc,
                    ev.event_code AS EventCode,
                    ev.payload_text AS PayloadText,
                    ev.protocol AS Protocol,
                    ev.message_type AS MessageType,
                    ev.packet_id AS PacketId,
                    ev.session_id AS SessionId
                FROM device_events ev
                INNER JOIN user_devices ud
                    ON ud.user_id = @UserId
                   AND ud.imei = @Imei
                   AND ud.is_active = 1
                   AND ud.imei = ev.imei
                WHERE ev.received_at_utc >= @FromUtc
                  AND ev.received_at_utc <= @ToUtc
                ORDER BY COALESCE(ev.occurred_at_utc, ev.received_at_utc) DESC, ev.event_id DESC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    ev.event_id AS "EventId",
                    COALESCE(ev.occurred_at_utc, ev.received_at_utc) AS "OccurredAtUtc",
                    ev.received_at_utc AS "ReceivedAtUtc",
                    ev.event_code AS "EventCode",
                    ev.payload_text AS "PayloadText",
                    ev.protocol AS "Protocol",
                    ev.message_type AS "MessageType",
                    ev.packet_id AS "PacketId",
                    ev.session_id AS "SessionId"
                FROM device_events ev
                INNER JOIN user_devices ud
                    ON ud.user_id = @UserId
                   AND ud.imei = @Imei
                   AND ud.is_active = TRUE
                   AND ud.imei = ev.imei
                WHERE ev.received_at_utc >= @FromUtc
                  AND ev.received_at_utc <= @ToUtc
                ORDER BY COALESCE(ev.occurred_at_utc, ev.received_at_utc) DESC, ev.event_id DESC
                LIMIT @Limit;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetTelemetryTripCandidatesSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT TOP (@MaxPoints)
                    p.gps_time_utc AS OccurredAtUtc,
                    rp.received_at_utc AS ReceivedAtUtc,
                    p.gps_time_utc AS GpsTimeUtc,
                    CAST(p.latitude AS FLOAT) AS Latitude,
                    CAST(p.longitude AS FLOAT) AS Longitude,
                    p.speed_kmh AS SpeedKmh,
                    p.heading_deg AS HeadingDeg,
                    p.packet_id AS PacketId,
                    p.session_id AS SessionId
                FROM positions p
                INNER JOIN raw_packets rp
                    ON rp.packet_id = p.packet_id
                INNER JOIN user_devices ud
                    ON ud.user_id = @UserId
                   AND ud.imei = @Imei
                   AND ud.is_active = 1
                   AND ud.imei = p.imei
                WHERE p.gps_time_utc >= @FromUtc
                  AND p.gps_time_utc <= @ToUtc
                  AND p.latitude IS NOT NULL
                  AND p.longitude IS NOT NULL
                ORDER BY p.gps_time_utc ASC, rp.received_at_utc ASC, p.position_id ASC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    p.gps_time_utc AS "OccurredAtUtc",
                    rp.received_at_utc AS "ReceivedAtUtc",
                    p.gps_time_utc AS "GpsTimeUtc",
                    p.latitude::DOUBLE PRECISION AS "Latitude",
                    p.longitude::DOUBLE PRECISION AS "Longitude",
                    p.speed_kmh AS "SpeedKmh",
                    p.heading_deg AS "HeadingDeg",
                    p.packet_id AS "PacketId",
                    p.session_id AS "SessionId"
                FROM positions p
                INNER JOIN raw_packets rp
                    ON rp.packet_id = p.packet_id
                INNER JOIN user_devices ud
                    ON ud.user_id = @UserId
                   AND ud.imei = @Imei
                   AND ud.is_active = TRUE
                   AND ud.imei = p.imei
                WHERE p.gps_time_utc >= @FromUtc
                  AND p.gps_time_utc <= @ToUtc
                  AND p.latitude IS NOT NULL
                  AND p.longitude IS NOT NULL
                ORDER BY p.gps_time_utc ASC, rp.received_at_utc ASC, p.position_id ASC
                LIMIT @MaxPoints;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
        };
    }

    private static string GetAccEventsForWindowSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT
                    COALESCE(ev.occurred_at_utc, ev.received_at_utc) AS OccurredAtUtc,
                    ev.event_code AS EventCode
                FROM device_events ev
                WHERE ev.imei = @Imei
                  AND ev.event_code IN ('ACC_ON', 'ACC_OFF')
                  AND COALESCE(ev.occurred_at_utc, ev.received_at_utc) BETWEEN @FromUtc AND @ToUtc
                ORDER BY COALESCE(ev.occurred_at_utc, ev.received_at_utc) ASC;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT
                    COALESCE(ev.occurred_at_utc, ev.received_at_utc) AS "OccurredAtUtc",
                    ev.event_code AS "EventCode"
                FROM device_events ev
                WHERE ev.imei = @Imei
                  AND ev.event_code IN ('ACC_ON', 'ACC_OFF')
                  AND COALESCE(ev.occurred_at_utc, ev.received_at_utc) BETWEEN @FromUtc AND @ToUtc
                ORDER BY COALESCE(ev.occurred_at_utc, ev.received_at_utc) ASC;
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

    private static string GetUpsertRawPacketSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                UPDATE raw_packets
                SET session_id = @SessionId,
                    device_id = COALESCE(@DeviceId, device_id),
                    imei = COALESCE(@Imei, imei),
                    port = @Port,
                    remote_ip = @RemoteIp,
                    protocol = @Protocol,
                    message_type = @MessageType,
                    payload_text = CASE WHEN LEN(@PayloadText) > 0 THEN @PayloadText ELSE payload_text END,
                    received_at_utc = CASE
                        WHEN @ReceivedAtUtc < received_at_utc THEN @ReceivedAtUtc
                        ELSE received_at_utc
                    END,
                    parse_status = @ParseStatus,
                    parse_error = COALESCE(@ParseError, parse_error),
                    ack_sent = CASE
                        WHEN @AckSent = 1 OR ack_sent = 1 THEN 1
                        ELSE 0
                    END,
                    ack_payload = COALESCE(@AckPayload, ack_payload),
                    ack_at_utc = COALESCE(@AckAtUtc, ack_at_utc),
                    ack_latency_ms = COALESCE(@AckLatencyMs, ack_latency_ms),
                    queue_backlog = CASE
                        WHEN @QueueBacklog > 0 THEN @QueueBacklog
                        ELSE queue_backlog
                    END
                WHERE packet_id = @PacketId;

                IF @@ROWCOUNT = 0
                BEGIN
                    BEGIN TRY
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
                    END TRY
                    BEGIN CATCH
                        IF ERROR_NUMBER() IN (2601, 2627)
                        BEGIN
                            UPDATE raw_packets
                            SET session_id = @SessionId,
                                device_id = COALESCE(@DeviceId, device_id),
                                imei = COALESCE(@Imei, imei),
                                port = @Port,
                                remote_ip = @RemoteIp,
                                protocol = @Protocol,
                                message_type = @MessageType,
                                payload_text = CASE WHEN LEN(@PayloadText) > 0 THEN @PayloadText ELSE payload_text END,
                                received_at_utc = CASE
                                    WHEN @ReceivedAtUtc < received_at_utc THEN @ReceivedAtUtc
                                    ELSE received_at_utc
                                END,
                                parse_status = @ParseStatus,
                                parse_error = COALESCE(@ParseError, parse_error),
                                ack_sent = CASE
                                    WHEN @AckSent = 1 OR ack_sent = 1 THEN 1
                                    ELSE 0
                                END,
                                ack_payload = COALESCE(@AckPayload, ack_payload),
                                ack_at_utc = COALESCE(@AckAtUtc, ack_at_utc),
                                ack_latency_ms = COALESCE(@AckLatencyMs, ack_latency_ms),
                                queue_backlog = CASE
                                    WHEN @QueueBacklog > 0 THEN @QueueBacklog
                                    ELSE queue_backlog
                                END
                            WHERE packet_id = @PacketId;
                        END
                        ELSE
                        BEGIN
                            THROW;
                        END
                    END CATCH
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
                ON CONFLICT (packet_id)
                DO UPDATE SET
                    session_id = EXCLUDED.session_id,
                    device_id = COALESCE(EXCLUDED.device_id, raw_packets.device_id),
                    imei = COALESCE(EXCLUDED.imei, raw_packets.imei),
                    port = EXCLUDED.port,
                    remote_ip = EXCLUDED.remote_ip,
                    protocol = EXCLUDED.protocol,
                    message_type = EXCLUDED.message_type,
                    payload_text = CASE
                        WHEN LENGTH(EXCLUDED.payload_text) > 0 THEN EXCLUDED.payload_text
                        ELSE raw_packets.payload_text
                    END,
                    received_at_utc = LEAST(raw_packets.received_at_utc, EXCLUDED.received_at_utc),
                    parse_status = EXCLUDED.parse_status,
                    parse_error = COALESCE(EXCLUDED.parse_error, raw_packets.parse_error),
                    ack_sent = raw_packets.ack_sent OR EXCLUDED.ack_sent,
                    ack_payload = COALESCE(EXCLUDED.ack_payload, raw_packets.ack_payload),
                    ack_at_utc = COALESCE(EXCLUDED.ack_at_utc, raw_packets.ack_at_utc),
                    ack_latency_ms = COALESCE(EXCLUDED.ack_latency_ms, raw_packets.ack_latency_ms),
                    queue_backlog = CASE
                        WHEN EXCLUDED.queue_backlog > 0 THEN EXCLUDED.queue_backlog
                        ELSE raw_packets.queue_backlog
                    END;
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

    private static string GetActiveUserIdsByImeiSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                SELECT ud.user_id
                FROM user_devices ud
                WHERE ud.imei = @Imei
                  AND ud.is_active = 1;
                """,
            DatabaseProvider.Postgres =>
                """
                SELECT ud.user_id
                FROM user_devices ud
                WHERE ud.imei = @Imei
                  AND ud.is_active = TRUE;
                """,
            _ => throw new InvalidOperationException("database_provider_not_supported_for_query")
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

        public int TotalCount { get; set; }
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

    private sealed class PlanRow
    {
        public Guid PlanId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int MaxGps { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class TelemetryDeviceSummaryRow
    {
        public string Imei { get; set; } = string.Empty;

        public DateTimeOffset BoundAtUtc { get; set; }

        public DateTimeOffset? LastSeenAtUtc { get; set; }

        public Guid? ActiveSessionId { get; set; }

        public int? Protocol { get; set; }

        public int? LastMessageType { get; set; }

        public DateTimeOffset? LastPositionOccurredAtUtc { get; set; }

        public DateTimeOffset? LastPositionReceivedAtUtc { get; set; }

        public DateTimeOffset? LastPositionGpsTimeUtc { get; set; }

        public double? LastPositionLatitude { get; set; }

        public double? LastPositionLongitude { get; set; }

        public double? LastPositionSpeedKmh { get; set; }

        public int? LastPositionHeadingDeg { get; set; }

        public Guid? LastPositionPacketId { get; set; }

        public Guid? LastPositionSessionId { get; set; }

        public string? Alias { get; set; }
    }

    private sealed class TelemetryPositionRow
    {
        public DateTimeOffset OccurredAtUtc { get; set; }

        public DateTimeOffset ReceivedAtUtc { get; set; }

        public DateTimeOffset? GpsTimeUtc { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public double? SpeedKmh { get; set; }

        public int? HeadingDeg { get; set; }

        public Guid PacketId { get; set; }

        public Guid SessionId { get; set; }
    }

    private sealed class TelemetryEventRow
    {
        public Guid EventId { get; set; }

        public DateTimeOffset OccurredAtUtc { get; set; }

        public DateTimeOffset ReceivedAtUtc { get; set; }

        public string EventCode { get; set; } = string.Empty;

        public string PayloadText { get; set; } = string.Empty;

        public int Protocol { get; set; }

        public int MessageType { get; set; }

        public Guid PacketId { get; set; }

        public Guid SessionId { get; set; }
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

        public string? Alias { get; set; }
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

    private sealed class AccEventRow
    {
        public DateTimeOffset OccurredAtUtc { get; set; }

        public string EventCode { get; set; } = string.Empty;
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
