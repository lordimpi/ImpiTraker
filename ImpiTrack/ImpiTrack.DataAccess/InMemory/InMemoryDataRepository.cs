using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Queue;
using System.Collections.Concurrent;

namespace ImpiTrack.DataAccess.InMemory;

/// <summary>
/// Repositorio en memoria para pruebas locales cuando no hay base de datos SQL configurada.
/// </summary>
public sealed class InMemoryDataRepository : IOpsRepository, IIngestionRepository, IUserAccountRepository, ITelemetryQueryRepository
{
    private const string DefaultPlanCode = "BASIC";
    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50, 100];
    private static readonly IReadOnlyList<AdminPlanDto> Plans =
    [
        new AdminPlanDto(Guid.Parse("10000000-0000-0000-0000-000000000001"), "BASIC", "Basic", 3, true),
        new AdminPlanDto(Guid.Parse("10000000-0000-0000-0000-000000000002"), "PRO", "Pro", 10, true),
        new AdminPlanDto(Guid.Parse("10000000-0000-0000-0000-000000000003"), "ENTERPRISE", "Enterprise", 100, true)
    ];

    private readonly ConcurrentDictionary<Guid, InMemoryUserAccount> _accounts = new();
    private readonly ConcurrentDictionary<string, Guid> _activeImeiOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<StoredPosition>> _positionsByImei = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _eventsByImei = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly IOpsDataStore _store;

    /// <summary>
    /// Crea un repositorio en memoria con almacenamiento operativo compartido.
    /// </summary>
    /// <param name="store">Almacen operativo en memoria.</param>
    public InMemoryDataRepository(IOpsDataStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task AddRawPacketAsync(RawPacketRecord record, long backlog, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.AddRawPacket(record, backlog);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertSessionAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.UpsertSession(session);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PersistEnvelopeResult> PersistEnvelopeAsync(InboundEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? imei = envelope.Message.Imei?.Trim();
        if (string.IsNullOrWhiteSpace(imei) || !_activeImeiOwners.ContainsKey(imei))
        {
            return Task.FromResult(new PersistEnvelopeResult(PersistEnvelopeStatus.SkippedUnownedDevice));
        }

        lock (_sync)
        {
            if (envelope.Message.MessageType == MessageType.Tracking)
            {
                if (!envelope.Message.IsTelemetryUsable)
                {
                    return Task.FromResult(new PersistEnvelopeResult(PersistEnvelopeStatus.Persisted));
                }

                List<StoredPosition> positions = _positionsByImei.GetOrAdd(imei, static _ => []);
                positions.Add(new StoredPosition(
                    envelope.PacketId.Value,
                    envelope.SessionId.Value,
                    envelope.Message.Protocol,
                    envelope.Message.MessageType,
                    envelope.Message.GpsTimeUtc ?? envelope.Message.ReceivedAtUtc,
                    envelope.Message.ReceivedAtUtc,
                    envelope.Message.GpsTimeUtc,
                    envelope.Message.Latitude,
                    envelope.Message.Longitude,
                    envelope.Message.SpeedKmh,
                    envelope.Message.HeadingDeg));

                return Task.FromResult(new PersistEnvelopeResult(PersistEnvelopeStatus.Persisted));
            }

            List<StoredEvent> events = _eventsByImei.GetOrAdd(imei, static _ => []);
            events.Add(new StoredEvent(
                Guid.NewGuid(),
                envelope.PacketId.Value,
                envelope.SessionId.Value,
                envelope.Message.Protocol,
                envelope.Message.MessageType,
                envelope.Message.MessageType.ToString(),
                envelope.Message.Text,
                envelope.Message.ReceivedAtUtc));

            return Task.FromResult(new PersistEnvelopeResult(PersistEnvelopeStatus.Persisted));
        }
    }

    /// <inheritdoc />
    public Task InsertDeviceIoEventAsync(DeviceIoEventRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string imei = record.Imei.Trim();
        if (string.IsNullOrWhiteSpace(imei) || !_activeImeiOwners.ContainsKey(imei))
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            List<StoredEvent> events = _eventsByImei.GetOrAdd(imei, static _ => []);
            events.Add(new StoredEvent(
                Guid.NewGuid(),
                record.PacketId.Value,
                record.SessionId.Value,
                record.Protocol,
                MessageType.Tracking,
                record.EventCode,
                record.PayloadText,
                record.ReceivedAtUtc));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PagedResult<RawPacketRecord>> GetLatestRawPacketsAsync(OpsRawListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);

        IReadOnlyList<RawPacketRecord> all = _store.GetLatestRawPackets(query.Imei, int.MaxValue);
        int totalItems = all.Count;
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        RawPacketRecord[] items = all.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return Task.FromResult(new PagedResult<RawPacketRecord>(items, page, pageSize, totalItems, totalPages));
    }

    /// <inheritdoc />
    public Task<RawPacketRecord?> GetRawPacketByIdAsync(PacketId packetId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryGetRawPacket(packetId, out RawPacketRecord? record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<PagedResult<ErrorAggregate>> GetTopErrorsAsync(OpsErrorListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int page = Math.Max(query.Page, 1);
        int pageSize = AllowedPageSizes.Contains(query.PageSize) ? query.PageSize : 20;

        DateTimeOffset toUtc = query.To ?? DateTimeOffset.UtcNow;
        DateTimeOffset fromUtc = query.From ?? toUtc.AddHours(-1);

        IReadOnlyList<ErrorAggregate> all = _store.GetTopErrors(fromUtc, toUtc, query.GroupBy, int.MaxValue);
        int totalItems = all.Count;
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        ErrorAggregate[] items = all.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return Task.FromResult(new PagedResult<ErrorAggregate>(items, page, pageSize, totalItems, totalPages));
    }

    /// <inheritdoc />
    public Task<PagedResult<SessionRecord>> GetActiveSessionsAsync(OpsSessionListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);

        IReadOnlyList<SessionRecord> all = _store.GetActiveSessions(query.Port);
        int totalItems = all.Count;
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        SessionRecord[] items = all.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return Task.FromResult(new PagedResult<SessionRecord>(items, page, pageSize, totalItems, totalPages));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PortIngestionSnapshot>> GetPortSnapshotsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetPortSnapshots());
    }

    /// <inheritdoc />
    public Task EnsureUserProvisioningAsync(
        Guid userId,
        string email,
        string? fullName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _accounts.AddOrUpdate(
            userId,
            _ => CreateAccount(userId, email, fullName, nowUtc),
            (_, existing) =>
            {
                existing.Email = email;
                existing.FullName = fullName ?? existing.FullName;
                return existing;
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<UserAccountSummary?> GetUserSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult<UserAccountSummary?>(null);
        }

        var summary = new UserAccountSummary(
            account.UserId,
            account.Email,
            account.FullName,
            account.PlanCode,
            account.PlanName,
            account.MaxGps,
            account.Devices.Count);

        return Task.FromResult<UserAccountSummary?>(summary);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserDeviceBinding>> GetUserDevicesAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult<IReadOnlyList<UserDeviceBinding>>([]);
        }

        IReadOnlyList<UserDeviceBinding> items = account.Devices
            .OrderByDescending(x => x.BoundAtUtc)
            .ThenBy(x => x.Imei, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public Task<PagedResult<UserDeviceBinding>> GetUserDevicesPagedAsync(Guid userId, AdminDeviceListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(new PagedResult<UserDeviceBinding>([], page, pageSize, 0, 0));
        }

        bool descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        string sortKey = query.SortBy.Trim().ToLowerInvariant();

        IEnumerable<UserDeviceBinding> filtered = account.Devices;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string s = query.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(x =>
                x.Imei.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (x.Alias != null && x.Alias.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        IOrderedEnumerable<UserDeviceBinding> ordered = sortKey switch
        {
            "imei" => descending
                ? filtered.OrderByDescending(x => x.Imei, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(x => x.Imei, StringComparer.OrdinalIgnoreCase),
            "alias" => descending
                ? filtered.OrderByDescending(x => x.Alias ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(x => x.Alias ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? filtered.OrderByDescending(x => x.BoundAtUtc)
                : filtered.OrderBy(x => x.BoundAtUtc)
        };

        int totalItems = filtered.Count();
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        IReadOnlyList<UserDeviceBinding> items = ordered
            .ThenBy(x => x.Imei, StringComparer.OrdinalIgnoreCase)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(new PagedResult<UserDeviceBinding>(items, page, pageSize, totalItems, totalPages));
    }

    /// <inheritdoc />
    public Task<PagedResult<UserDeviceBinding>> GetUserDevicesPagedMeAsync(Guid userId, MeDeviceListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(new PagedResult<UserDeviceBinding>([], page, pageSize, 0, 0));
        }

        IEnumerable<UserDeviceBinding> meFiltered = account.Devices;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string s = query.Search.Trim().ToLowerInvariant();
            meFiltered = meFiltered.Where(x =>
                x.Imei.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (x.Alias != null && x.Alias.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        int totalItems = meFiltered.Count();
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        IReadOnlyList<UserDeviceBinding> items = meFiltered
            .OrderByDescending(x => x.BoundAtUtc)
            .ThenBy(x => x.Imei, StringComparer.OrdinalIgnoreCase)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(new PagedResult<UserDeviceBinding>(items, page, pageSize, totalItems, totalPages));
    }

    /// <inheritdoc />
    public Task<BindDeviceResult> BindDeviceAsync(
        Guid userId,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(new BindDeviceResult(BindDeviceStatus.MissingActivePlan, null));
        }

        string normalizedImei = imei.Trim();
        if (normalizedImei.Length == 0)
        {
            return Task.FromResult(new BindDeviceResult(BindDeviceStatus.OwnedByAnotherUser, null));
        }

        lock (_sync)
        {
            UserDeviceBinding? existing = account.Devices
                .FirstOrDefault(x => string.Equals(x.Imei, normalizedImei, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return Task.FromResult(new BindDeviceResult(BindDeviceStatus.AlreadyBound, existing.DeviceId));
            }

            if (_activeImeiOwners.TryGetValue(normalizedImei, out Guid ownerId) && ownerId != userId)
            {
                return Task.FromResult(new BindDeviceResult(BindDeviceStatus.OwnedByAnotherUser, null));
            }

            if (account.Devices.Count >= account.MaxGps)
            {
                return Task.FromResult(new BindDeviceResult(BindDeviceStatus.QuotaExceeded, null));
            }

            Guid deviceId = Guid.NewGuid();
            account.Devices.Add(new UserDeviceBinding(deviceId, normalizedImei, nowUtc));
            _activeImeiOwners[normalizedImei] = userId;
            return Task.FromResult(new BindDeviceResult(BindDeviceStatus.Bound, deviceId));
        }
    }

    /// <inheritdoc />
    public Task<bool> UnbindDeviceAsync(
        Guid userId,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = nowUtc;

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(false);
        }

        lock (_sync)
        {
            UserDeviceBinding? existing = account.Devices
                .FirstOrDefault(x => string.Equals(x.Imei, imei, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return Task.FromResult(false);
            }

            account.Devices.Remove(existing);
            _activeImeiOwners.TryRemove(existing.Imei, out _);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<PagedResult<UserAccountOverview>> GetUsersAsync(AdminUserListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        string? search = query.Search?.Trim();
        string? planCode = string.IsNullOrWhiteSpace(query.PlanCode)
            ? null
            : query.PlanCode.Trim().ToUpperInvariant();
        string sortBy = query.SortBy.Trim();
        bool descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        IEnumerable<InMemoryUserAccount> filtered = _accounts.Values;

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(x =>
                x.Email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(x.FullName) &&
                 x.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (planCode is not null)
        {
            filtered = filtered.Where(x => string.Equals(x.PlanCode, planCode, StringComparison.OrdinalIgnoreCase));
        }

        IEnumerable<InMemoryUserAccount> ordered = ApplyOrdering(filtered, sortBy, descending);
        int totalItems = filtered.Count();
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        IReadOnlyList<UserAccountOverview> rows = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToOverview)
            .ToArray();

        return Task.FromResult(new PagedResult<UserAccountOverview>(rows, page, pageSize, totalItems, totalPages));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AdminPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AdminPlanDto> rows = Plans
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TelemetryDeviceSummaryDto>> GetDeviceSummariesAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult<IReadOnlyList<TelemetryDeviceSummaryDto>>([]);
        }

        IReadOnlyList<SessionRecord> activeSessions = _store.GetActiveSessions(port: null);
        IReadOnlyList<TelemetryDeviceSummaryDto> rows = account.Devices
            .OrderByDescending(x => x.BoundAtUtc)
            .ThenBy(x => x.Imei, StringComparer.OrdinalIgnoreCase)
            .Select(device => BuildTelemetrySummary(device, activeSessions))
            .ToArray();

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<bool> HasActiveDeviceBindingAsync(Guid userId, string imei, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(false);
        }

        bool exists = account.Devices.Any(x => string.Equals(x.Imei, imei, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DevicePositionPointDto>> GetPositionsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_accounts.TryGetValue(userId, out _))
        {
            return Task.FromResult<IReadOnlyList<DevicePositionPointDto>>([]);
        }

        if (!_positionsByImei.TryGetValue(imei, out List<StoredPosition>? positions))
        {
            return Task.FromResult<IReadOnlyList<DevicePositionPointDto>>([]);
        }

        IReadOnlyList<DevicePositionPointDto> rows = positions
            .Where(x =>
                x.Latitude.HasValue &&
                x.Longitude.HasValue &&
                x.OccurredAtUtc >= fromUtc &&
                x.OccurredAtUtc <= toUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.ReceivedAtUtc)
            .Take(limit)
            .Select(x => new DevicePositionPointDto(
                x.OccurredAtUtc,
                x.ReceivedAtUtc,
                x.GpsTimeUtc,
                x.Latitude!.Value,
                x.Longitude!.Value,
                x.SpeedKmh,
                x.HeadingDeg,
                x.PacketId,
                x.SessionId))
            .ToArray();

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeviceEventDto>> GetEventsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_accounts.TryGetValue(userId, out _))
        {
            return Task.FromResult<IReadOnlyList<DeviceEventDto>>([]);
        }

        if (!_eventsByImei.TryGetValue(imei, out List<StoredEvent>? events))
        {
            return Task.FromResult<IReadOnlyList<DeviceEventDto>>([]);
        }

        IReadOnlyList<DeviceEventDto> rows = events
            .Where(x => x.ReceivedAtUtc >= fromUtc && x.ReceivedAtUtc <= toUtc)
            .OrderByDescending(x => x.ReceivedAtUtc)
            .ThenByDescending(x => x.EventId)
            .Take(limit)
            .Select(x => new DeviceEventDto(
                x.EventId,
                x.ReceivedAtUtc,
                x.ReceivedAtUtc,
                x.EventCode,
                x.PayloadText,
                x.Protocol,
                x.MessageType,
                x.PacketId,
                x.SessionId))
            .ToArray();

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DevicePositionPointDto>> GetTripCandidatePositionsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_accounts.TryGetValue(userId, out _))
        {
            return Task.FromResult<IReadOnlyList<DevicePositionPointDto>>([]);
        }

        if (!_positionsByImei.TryGetValue(imei, out List<StoredPosition>? positions))
        {
            return Task.FromResult<IReadOnlyList<DevicePositionPointDto>>([]);
        }

        IReadOnlyList<DevicePositionPointDto> rows = positions
            .Where(x =>
                x.Latitude.HasValue &&
                x.Longitude.HasValue &&
                x.OccurredAtUtc >= fromUtc &&
                x.OccurredAtUtc <= toUtc)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.ReceivedAtUtc)
            .Take(maxPoints)
            .Select(x => new DevicePositionPointDto(
                x.OccurredAtUtc,
                x.ReceivedAtUtc,
                x.GpsTimeUtc,
                x.Latitude!.Value,
                x.Longitude!.Value,
                x.SpeedKmh,
                x.HeadingDeg,
                x.PacketId,
                x.SessionId))
            .ToArray();

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> GetActiveUserIdsByImeiAsync(string imei, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedImei = imei.Trim();
        List<Guid> userIds = [];

        foreach (KeyValuePair<Guid, InMemoryUserAccount> kvp in _accounts)
        {
            bool ownsDevice = kvp.Value.Devices
                .Any(d => string.Equals(d.Imei, normalizedImei, StringComparison.OrdinalIgnoreCase));
            if (ownsDevice)
            {
                userIds.Add(kvp.Key);
            }
        }

        return Task.FromResult<IReadOnlyList<Guid>>(userIds);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccEventDto>> GetAccEventsForWindowAsync(
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // El repositorio en memoria retorna lista vacia: los eventos ACC en memoria
        // estan almacenados en _eventsByImei pero no tienen una estructura que
        // permita filtrar por event_code de forma eficiente. El algoritmo de BuildTrips
        // detecta correctamente IgnitionOn=null y cae en modo fallback de velocidad+2D.
        return Task.FromResult<IReadOnlyList<AccEventDto>>([]);
    }

    /// <inheritdoc />
    public Task<bool> SetUserPlanAsync(
        Guid userId,
        string planCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = nowUtc;

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(false);
        }

        if (!TryResolvePlan(planCode, out AdminPlanDto? plan) || plan is null)
        {
            return Task.FromResult(false);
        }

        account.PlanCode = plan.Code;
        account.PlanName = plan.Name;
        account.MaxGps = plan.MaxGps;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> UpdateDeviceAliasAsync(
        Guid userId,
        string imei,
        string? alias,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(false);
        }

        lock (_sync)
        {
            UserDeviceBinding? existing = account.Devices
                .FirstOrDefault(x => string.Equals(x.Imei, imei, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return Task.FromResult(false);
            }

            int index = account.Devices.IndexOf(existing);
            account.Devices[index] = existing with { Alias = alias };
            return Task.FromResult(true);
        }
    }

    private static InMemoryUserAccount CreateAccount(Guid userId, string email, string? fullName, DateTimeOffset nowUtc)
    {
        if (!TryResolvePlan(DefaultPlanCode, out AdminPlanDto? plan) || plan is null)
        {
            throw new InvalidOperationException("default_plan_not_configured");
        }

        return new InMemoryUserAccount
        {
            UserId = userId,
            Email = email,
            FullName = fullName,
            PlanCode = plan.Code,
            PlanName = plan.Name,
            MaxGps = plan.MaxGps,
            CreatedAtUtc = nowUtc
        };
    }

    private static bool TryResolvePlan(string planCode, out AdminPlanDto? plan)
    {
        string normalizedCode = planCode.Trim().ToUpperInvariant();
        plan = Plans.FirstOrDefault(x => string.Equals(x.Code, normalizedCode, StringComparison.OrdinalIgnoreCase) && x.IsActive);
        return plan is not null;
    }

    private static UserAccountOverview ToOverview(InMemoryUserAccount account)
    {
        return new UserAccountOverview(
            account.UserId,
            account.Email,
            account.FullName,
            account.PlanCode,
            account.MaxGps,
            account.Devices.Count);
    }

    private static IEnumerable<InMemoryUserAccount> ApplyOrdering(
        IEnumerable<InMemoryUserAccount> source,
        string sortBy,
        bool descending)
    {
        return sortBy.ToLowerInvariant() switch
        {
            "fullname" => descending
                ? source.OrderByDescending(x => x.FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            "plancode" => descending
                ? source.OrderByDescending(x => x.PlanCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.PlanCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            "maxgps" => descending
                ? source.OrderByDescending(x => x.MaxGps).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.MaxGps).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            "usedgps" => descending
                ? source.OrderByDescending(x => x.Devices.Count).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.Devices.Count).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            "createdat" => descending
                ? source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? source.OrderByDescending(x => x.Email, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.UserId)
                : source.OrderBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.UserId)
        };
    }

    private TelemetryDeviceSummaryDto BuildTelemetrySummary(UserDeviceBinding device, IReadOnlyList<SessionRecord> activeSessions)
    {
        StoredPosition? latestPosition = _positionsByImei.TryGetValue(device.Imei, out List<StoredPosition>? positions)
            ? positions
                .Where(x => x.Latitude.HasValue && x.Longitude.HasValue)
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.ReceivedAtUtc)
                .FirstOrDefault()
            : null;

        StoredPosition? latestPositionAny = _positionsByImei.TryGetValue(device.Imei, out positions)
            ? positions
                .OrderByDescending(x => x.ReceivedAtUtc)
                .ThenByDescending(x => x.OccurredAtUtc)
                .FirstOrDefault()
            : null;

        StoredEvent? latestEvent = _eventsByImei.TryGetValue(device.Imei, out List<StoredEvent>? events)
            ? events
                .OrderByDescending(x => x.ReceivedAtUtc)
                .ThenByDescending(x => x.EventId)
                .FirstOrDefault()
            : null;

        SessionRecord? activeSession = activeSessions
            .Where(x => string.Equals(x.Imei, device.Imei, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastSeenAtUtc)
            .ThenBy(x => x.SessionId.Value)
            .FirstOrDefault();

        DateTimeOffset? latestSeen = activeSession?.LastSeenAtUtc;
        if (latestPositionAny is not null && (!latestSeen.HasValue || latestPositionAny.ReceivedAtUtc > latestSeen.Value))
        {
            latestSeen = latestPositionAny.ReceivedAtUtc;
        }

        if (latestEvent is not null && (!latestSeen.HasValue || latestEvent.ReceivedAtUtc > latestSeen.Value))
        {
            latestSeen = latestEvent.ReceivedAtUtc;
        }

        ProtocolId? protocol = null;
        MessageType? messageType = null;
        DateTimeOffset? latestMessageAtUtc = null;

        if (latestPositionAny is not null)
        {
            latestMessageAtUtc = latestPositionAny.ReceivedAtUtc;
            protocol = latestPositionAny.Protocol;
            messageType = latestPositionAny.MessageType;
        }

        if (latestEvent is not null && (!latestMessageAtUtc.HasValue || latestEvent.ReceivedAtUtc > latestMessageAtUtc.Value))
        {
            latestMessageAtUtc = latestEvent.ReceivedAtUtc;
            protocol = latestEvent.Protocol;
            messageType = latestEvent.MessageType;
        }

        LastKnownPositionDto? lastPosition = latestPosition is null
            ? null
            : new LastKnownPositionDto(
                latestPosition.OccurredAtUtc,
                latestPosition.ReceivedAtUtc,
                latestPosition.GpsTimeUtc,
                latestPosition.Latitude!.Value,
                latestPosition.Longitude!.Value,
                latestPosition.SpeedKmh,
                latestPosition.HeadingDeg,
                latestPosition.PacketId,
                latestPosition.SessionId);

        return new TelemetryDeviceSummaryDto(
            device.Imei,
            device.BoundAtUtc,
            latestSeen,
            activeSession?.SessionId.Value,
            protocol,
            messageType,
            lastPosition,
            device.Alias);
    }

    private sealed class InMemoryUserAccount
    {
        public Guid UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public string PlanCode { get; set; } = "BASIC";

        public string PlanName { get; set; } = "Basic";

        public int MaxGps { get; set; } = 3;

        public DateTimeOffset CreatedAtUtc { get; set; }

        public List<UserDeviceBinding> Devices { get; } = [];
    }

    private sealed record StoredPosition(
        Guid PacketId,
        Guid SessionId,
        ProtocolId Protocol,
        MessageType MessageType,
        DateTimeOffset OccurredAtUtc,
        DateTimeOffset ReceivedAtUtc,
        DateTimeOffset? GpsTimeUtc,
        double? Latitude,
        double? Longitude,
        double? SpeedKmh,
        int? HeadingDeg);

    private sealed record StoredEvent(
        Guid EventId,
        Guid PacketId,
        Guid SessionId,
        ProtocolId Protocol,
        MessageType MessageType,
        string EventCode,
        string PayloadText,
        DateTimeOffset ReceivedAtUtc);
}
