using ImpiTrack.Application.Abstractions;
using ImpiTrack.Application.Services;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiTrack.Tests;

/// <summary>
/// Unit tests for DeviceCommandService orchestration logic.
/// Uses hand-rolled stubs to avoid complex mock setups.
/// </summary>
public sealed class DeviceCommandServiceTests
{
    private const string TestImei = "864035051929066";
    private static readonly Guid TestUserId = Guid.NewGuid();

    // ─── EnqueueAsync: happy path ─────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_HappyPath_Arm_ReturnsSuccessWithCommandId()
    {
        var (sut, repo, _) = BuildSut(isOwner: true, protocol: ProtocolId.Coban);

        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.Arm,
            null, false, "127.0.0.1", "TestAgent", CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.CommandId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Enqueue_HappyPath_Arm_InsertsCommandAsQueued()
    {
        var (sut, repo, _) = BuildSut(isOwner: true, protocol: ProtocolId.Coban);

        await sut.EnqueueAsync(TestImei, TestUserId, DeviceCommandType.Arm,
            null, false, "127.0.0.1", "TestAgent", CancellationToken.None);

        Assert.Single(repo.Inserted);
        DeviceCommandRecord inserted = repo.Inserted[0];
        Assert.Equal(TestImei, inserted.Imei);
        Assert.Equal("Arm", inserted.CommandType);
        // May be Queued or Sent depending on whether session is online
        Assert.True(inserted.Status == "Queued" || inserted.Status == "Sent");
    }

    // ─── EnqueueAsync: sad paths ──────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_NotOwner_ReturnsNotFoundError()
    {
        var (sut, _, _) = BuildSut(isOwner: false, protocol: ProtocolId.Coban);

        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.Arm,
            null, false, "127.0.0.1", "Test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("not_found", result.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_ProtocolUnknown_ReturnsProtocolError()
    {
        var (sut, _, _) = BuildSut(isOwner: true, protocol: null);

        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.Arm,
            null, false, "127.0.0.1", "Test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("protocol_unknown", result.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_CommandNotSupportedByProtocol_Coban_Reset_ReturnsError()
    {
        var (sut, _, _) = BuildSut(isOwner: true, protocol: ProtocolId.Coban);

        // Reset is a dangerous command — pass confirm: true so the dangerous-command gate
        // doesn't fire before the serializer-supports gate (which is the one under test).
        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.Reset,
            null, confirm: true, "127.0.0.1", "Test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("command_not_supported_by_protocol", result.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_CommandNotSupportedByProtocol_Cantrack_SetOverspeedAlarm_ReturnsError()
    {
        var (sut, _, _) = BuildSut(isOwner: true, protocol: ProtocolId.Cantrack);

        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.SetOverspeedAlarm,
            null, false, "127.0.0.1", "Test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("command_not_supported_by_protocol", result.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_DangerousCommandWithoutConfirm_CutMotor_ReturnsError()
    {
        var (sut, _, _) = BuildSut(isOwner: true, protocol: ProtocolId.Coban);

        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.CutMotor,
            null, confirm: false, "127.0.0.1", "Test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("confirmation_required", result.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_DangerousCommandWithConfirm_CutMotor_Succeeds()
    {
        var (sut, _, _) = BuildSut(isOwner: true, protocol: ProtocolId.Coban);

        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.CutMotor,
            null, confirm: true, "127.0.0.1", "Test", CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Enqueue_SetMovementAlarm_NoRadius_ReturnsInvalidParams()
    {
        var (sut, _, _) = BuildSut(isOwner: true, protocol: ProtocolId.Coban);

        // No parameters → validator rejects
        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.SetMovementAlarm,
            null, false, "127.0.0.1", "Test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_SetMovementAlarm_InvalidRadius_ReturnsInvalidParams()
    {
        var (sut, _, _) = BuildSut(isOwner: true, protocol: ProtocolId.Coban);

        // radiusMeters out of [1..99999]
        EnqueueResult result = await sut.EnqueueAsync(
            TestImei, TestUserId, DeviceCommandType.SetMovementAlarm,
            new Dictionary<string, object> { ["radiusMeters"] = 0 },
            false, "127.0.0.1", "Test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    // ─── HandleAckAsync: Coban FIFO match ────────────────────────────────────

    [Fact]
    public async Task HandleAck_Coban_FifoMatch_UpdatesOldestSentCommand()
    {
        var repo = new InMemoryCommandRepository();
        var notifier = new CapturingNotifier();
        var sut = BuildSutWithRepo(repo, notifier);

        Guid olderId = Guid.NewGuid();
        Guid newerId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Two Sent commands with same correlation_key — oldest must be matched first.
        repo.AddSent(new DeviceCommandRecord
        {
            CommandId = olderId,
            Imei = TestImei,
            UserId = TestUserId,
            CommandType = "CutMotor",
            Status = "Sent",
            CorrelationKey = "109",
            QueuedAtUtc = now.AddSeconds(-60),
            SentAtUtc = now.AddSeconds(-30)
        });
        repo.AddSent(new DeviceCommandRecord
        {
            CommandId = newerId,
            Imei = TestImei,
            UserId = TestUserId,
            CommandType = "CutMotor",
            Status = "Sent",
            CorrelationKey = "109",
            QueuedAtUtc = now.AddSeconds(-20),
            SentAtUtc = now.AddSeconds(-10)
        });

        await sut.HandleAckAsync(
            TestImei, ProtocolId.Coban,
            responseCode: "109",
            correlationKey: "109",
            correlationTimestamp: null,
            CancellationToken.None);

        // The older one must be transitioned
        Assert.Contains(repo.Updated, u => u.CommandId == olderId && u.Status == "Acknowledged");
        // The newer one must still be Sent
        Assert.DoesNotContain(repo.Updated, u => u.CommandId == newerId);
    }

    [Fact]
    public async Task HandleAck_Coban_509_MapsTo109_FindsCorrectCommand()
    {
        var repo = new InMemoryCommandRepository();
        var sut = BuildSutWithRepo(repo, new CapturingNotifier());

        Guid cmdId = Guid.NewGuid();
        repo.AddSent(new DeviceCommandRecord
        {
            CommandId = cmdId,
            Imei = TestImei,
            UserId = TestUserId,
            CommandType = "CutMotor",
            Status = "Sent",
            CorrelationKey = "109",
            QueuedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
            SentAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10)
        });

        // 509 = deferred cut motor → maps to keyword "109"
        await sut.HandleAckAsync(
            TestImei, ProtocolId.Coban,
            responseCode: "509",
            correlationKey: null,   // no explicit key — service derives from responseCode
            correlationTimestamp: null,
            CancellationToken.None);

        Assert.Contains(repo.Updated, u => u.CommandId == cmdId && u.Status == "Acknowledged");
    }

    // ─── HandleAckAsync: Cantrack exact match ────────────────────────────────

    [Fact]
    public async Task HandleAck_Cantrack_ExactMatch_UpdatesCorrectCommand()
    {
        var repo = new InMemoryCommandRepository();
        var sut = BuildSutWithRepo(repo, new CapturingNotifier());

        Guid cmdId = Guid.NewGuid();
        repo.AddSent(new DeviceCommandRecord
        {
            CommandId = cmdId,
            Imei = TestImei,
            UserId = TestUserId,
            CommandType = "CutMotor",
            Status = "Sent",
            CorrelationKey = "CMD",
            CorrelationTimestamp = "143022",
            QueuedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
            SentAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10)
        });

        await sut.HandleAckAsync(
            TestImei, ProtocolId.Cantrack,
            responseCode: "CMD",
            correlationKey: "CMD",
            correlationTimestamp: "143022",
            CancellationToken.None);

        Assert.Contains(repo.Updated, u => u.CommandId == cmdId && u.Status == "Acknowledged");
    }

    // ─── HandleAckAsync: no match → log warning, no exception ────────────────

    [Fact]
    public async Task HandleAck_NoMatch_DoesNotThrow()
    {
        var repo = new InMemoryCommandRepository(); // empty
        var sut = BuildSutWithRepo(repo, new CapturingNotifier());

        // Should complete silently — no commands to match
        Exception? ex = await Record.ExceptionAsync(() =>
            sut.HandleAckAsync(
                TestImei, ProtocolId.Coban,
                responseCode: "109",
                correlationKey: "109",
                correlationTimestamp: null,
                CancellationToken.None));

        Assert.Null(ex);
    }

    // ─── Notifier: called after status change ─────────────────────────────────

    [Fact]
    public async Task Enqueue_OnSuccess_NotifierIsCalled()
    {
        var repo = new InMemoryCommandRepository();
        var notifier = new CapturingNotifier();
        var sut = BuildSutWithRepo(repo, notifier,
            isOwner: true, protocol: ProtocolId.Coban);

        await sut.EnqueueAsync(TestImei, TestUserId, DeviceCommandType.Arm,
            null, false, "127.0.0.1", "Test", CancellationToken.None);

        Assert.NotEmpty(notifier.Notifications);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (DeviceCommandService sut, InMemoryCommandRepository repo, CapturingNotifier notifier)
        BuildSut(bool isOwner, ProtocolId? protocol)
    {
        var repo = new InMemoryCommandRepository();
        var notifier = new CapturingNotifier();
        var sut = BuildSutWithRepo(repo, notifier, isOwner, protocol);
        return (sut, repo, notifier);
    }

    private static DeviceCommandService BuildSutWithRepo(
        InMemoryCommandRepository repo,
        CapturingNotifier notifier,
        bool isOwner = true,
        ProtocolId? protocol = ProtocolId.Coban)
    {
        return new DeviceCommandService(
            repo,
            new DeviceCommandValidator(),
            new StubOwnershipGuard(isOwner),
            new StubProtocolResolver(protocol),
            notifier,
            new NoOpSessionManager(),
            [new ImpiTrack.Protocols.Coban.CobanCommandSerializer(),
             new ImpiTrack.Protocols.Cantrack.CantrackCommandSerializer()],
            NullLogger<DeviceCommandService>.Instance);
    }

    // ─── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubOwnershipGuard : IDeviceCommandOwnershipGuard
    {
        private readonly bool _isOwner;
        public StubOwnershipGuard(bool isOwner) => _isOwner = isOwner;
        public Task<bool> IsOwnerAsync(string imei, Guid userId, CancellationToken ct)
            => Task.FromResult(_isOwner);
    }

    private sealed class StubProtocolResolver : IDeviceProtocolResolver
    {
        private readonly ProtocolId? _protocol;
        public StubProtocolResolver(ProtocolId? protocol) => _protocol = protocol;
        public Task<ProtocolId?> ResolveForDeviceAsync(string imei, CancellationToken ct)
            => Task.FromResult(_protocol);
    }

    private sealed class CapturingNotifier : IDeviceCommandNotifier
    {
        public List<(Guid UserId, DeviceCommandRecord Command)> Notifications { get; } = new();

        public Task NotifyStatusChangedAsync(Guid userId, DeviceCommandRecord command, CancellationToken ct)
        {
            Notifications.Add((userId, command));
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpSessionManager : ISessionManager
    {
        public event Action<SessionState>? ImeiAttached;
        public SessionState Open(string remoteIp, int port) => throw new NotSupportedException();
        public void Touch(SessionId sessionId) { }
        public void AttachImei(SessionId sessionId, string? imei) { }
        public void MarkHeartbeat(SessionId sessionId) { }
        public void IncrementFramesIn(SessionId sessionId) { }
        public void IncrementFramesInvalid(SessionId sessionId) { }
        public void SetCloseReason(SessionId sessionId, string closeReason) { }
        public bool TryGet(SessionId sessionId, out SessionState? session) { session = null; return false; }
        public bool Close(SessionId sessionId) => false;
        public bool TryGetByImei(string imei, out SessionState? session) { session = null; return false; }
        public bool TryEnqueueCommand(string imei, OutboundFrame frame) => false;
    }

    /// <summary>
    /// In-memory fake repository for DeviceCommandService tests.
    /// Tracks inserted + updated records. Implements FIFO for MatchPendingByKeywordAsync.
    /// </summary>
    private sealed class InMemoryCommandRepository : IDeviceCommandRepository
    {
        private readonly List<DeviceCommandRecord> _store = new();
        public List<DeviceCommandRecord> Inserted { get; } = new();
        public List<DeviceCommandRecord> Updated { get; } = new();

        public void AddSent(DeviceCommandRecord r) => _store.Add(r);

        public Task<Guid> InsertAsync(DeviceCommandRecord record, CancellationToken ct)
        {
            _store.Add(record);
            Inserted.Add(record);
            return Task.FromResult(record.CommandId);
        }

        public Task<bool> UpdateStatusAsync(
            Guid commandId, string newStatus, DateTimeOffset? timestamp,
            string? payloadSent, string? correlationKey, string? correlationTimestamp,
            string? responseCode, string? responseText, string? failureReason,
            CancellationToken ct)
        {
            DeviceCommandRecord? r = _store.FirstOrDefault(x => x.CommandId == commandId);
            if (r is null) return Task.FromResult(false);

            // Don't update terminal states (matches production logic)
            if (r.Status is "Acknowledged" or "Failed" or "Timeout" or "InterruptedByRestart")
                return Task.FromResult(false);

            r.Status = newStatus;
            r.SentAtUtc = newStatus == "Sent" ? timestamp : r.SentAtUtc;
            r.AckedAtUtc = newStatus is "Acknowledged" or "Failed" ? timestamp : r.AckedAtUtc;
            r.ResponseCode = responseCode ?? r.ResponseCode;
            r.ResponseText = responseText ?? r.ResponseText;
            r.FailureReason = failureReason ?? r.FailureReason;
            Updated.Add(r);
            return Task.FromResult(true);
        }

        public Task<DeviceCommandRecord?> GetByIdAsync(Guid commandId, CancellationToken ct)
            => Task.FromResult(_store.FirstOrDefault(x => x.CommandId == commandId));

        public Task<IReadOnlyList<DeviceCommandRecord>> GetQueuedByImeiAsync(string imei, CancellationToken ct)
        {
            IReadOnlyList<DeviceCommandRecord> list = _store
                .Where(x => x.Imei == imei && x.Status == "Queued")
                .OrderBy(x => x.QueuedAtUtc)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<PagedResult<DeviceCommandRecord>> ListPagedAsync(
            string imei, Guid userId, DeviceCommandListQuery query, CancellationToken ct)
        {
            var items = _store
                .Where(x => x.Imei == imei && x.UserId == userId)
                .ToList();
            return Task.FromResult(new PagedResult<DeviceCommandRecord>(items, 1, 20, items.Count, 1));
        }

        public Task<IReadOnlyList<DeviceCommandRecord>> ScanStaleSentAsync(DateTimeOffset olderThan, CancellationToken ct)
        {
            IReadOnlyList<DeviceCommandRecord> list = _store
                .Where(x => x.Status == "Sent" && x.SentAtUtc < olderThan)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<DeviceCommandRecord?> MatchPendingByKeywordAsync(
            string imei, string correlationKey, CancellationToken ct)
        {
            // FIFO: return oldest Sent with matching key
            DeviceCommandRecord? result = _store
                .Where(x => x.Imei == imei && x.Status == "Sent" && x.CorrelationKey == correlationKey)
                .OrderBy(x => x.SentAtUtc)
                .FirstOrDefault();
            return Task.FromResult(result);
        }

        public Task<DeviceCommandRecord?> MatchPendingByCorrelationAsync(
            string imei, string correlationKey, string correlationTimestamp, CancellationToken ct)
        {
            DeviceCommandRecord? result = _store.FirstOrDefault(x =>
                x.Imei == imei &&
                x.Status == "Sent" &&
                x.CorrelationKey == correlationKey &&
                x.CorrelationTimestamp == correlationTimestamp);
            return Task.FromResult(result);
        }
    }
}
