using System.Text;
using ImpiTrack.Api.Hubs;
using ImpiTrack.Api.Services;
using ImpiTrack.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiTrack.Tests;

/// <summary>
/// Tests unitarios para SignalRTelemetryNotifier.
/// E.1: Verifica que los eventos se envian al grupo correcto y que no se envian a usuarios que no son propietarios.
/// </summary>
public sealed class SignalRTelemetryNotifierTests
{
    [Fact]
    public async Task NotifyPositionUpdated_SendsToCorrectUserGroup()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        var resolver = new StubOwnershipResolver(new Dictionary<string, IReadOnlyList<Guid>>
        {
            ["864035053671278"] = [userId]
        });

        var sentMessages = new List<(string GroupName, string Method, object? Arg)>();
        var hubContext = CreateMockHubContext(sentMessages);
        var notifier = new SignalRTelemetryNotifier(hubContext, resolver, NullLogger<SignalRTelemetryNotifier>.Instance);

        // Act
        await notifier.NotifyPositionUpdatedAsync(
            "864035053671278",
            -2.455,
            -76.591,
            45.5,
            180,
            DateTimeOffset.UtcNow,
            true,
            CancellationToken.None);

        // Assert
        Assert.Single(sentMessages);
        Assert.Equal($"user_{userId}", sentMessages[0].GroupName);
        Assert.Equal("PositionUpdated", sentMessages[0].Method);
        Assert.IsType<PositionUpdatedMessage>(sentMessages[0].Arg);

        var msg = (PositionUpdatedMessage)sentMessages[0].Arg!;
        Assert.Equal("864035053671278", msg.Imei);
        Assert.Equal(-2.455, msg.Latitude);
        Assert.Equal(-76.591, msg.Longitude);
        Assert.Equal(45.5, msg.SpeedKmh);
        Assert.Equal(180, msg.HeadingDeg);
        Assert.True(msg.IgnitionOn);
    }

    [Fact]
    public async Task NotifyPositionUpdated_DoesNotSendToNonOwner()
    {
        // Arrange — IMEI has no owners
        var resolver = new StubOwnershipResolver(new Dictionary<string, IReadOnlyList<Guid>>());
        var sentMessages = new List<(string GroupName, string Method, object? Arg)>();
        var hubContext = CreateMockHubContext(sentMessages);
        var notifier = new SignalRTelemetryNotifier(hubContext, resolver, NullLogger<SignalRTelemetryNotifier>.Instance);

        // Act
        await notifier.NotifyPositionUpdatedAsync(
            "999999999999999",
            0, 0, 0, 0,
            DateTimeOffset.UtcNow,
            false,
            CancellationToken.None);

        // Assert — no messages sent
        Assert.Empty(sentMessages);
    }

    [Fact]
    public async Task NotifyDeviceStatusChanged_SendsCorrectEvent()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        var resolver = new StubOwnershipResolver(new Dictionary<string, IReadOnlyList<Guid>>
        {
            ["864035053671278"] = [userId]
        });
        var sentMessages = new List<(string GroupName, string Method, object? Arg)>();
        var hubContext = CreateMockHubContext(sentMessages);
        var notifier = new SignalRTelemetryNotifier(hubContext, resolver, NullLogger<SignalRTelemetryNotifier>.Instance);

        // Act
        await notifier.NotifyDeviceStatusChangedAsync(
            "864035053671278",
            "Online",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert
        Assert.Single(sentMessages);
        Assert.Equal("DeviceStatusChanged", sentMessages[0].Method);
        var msg = (DeviceStatusChangedMessage)sentMessages[0].Arg!;
        Assert.Equal("Online", msg.Status);
    }

    [Fact]
    public async Task NotifyTelemetryEvent_SendsToMultipleOwners()
    {
        // Arrange
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        var resolver = new StubOwnershipResolver(new Dictionary<string, IReadOnlyList<Guid>>
        {
            ["864035053671278"] = [userA, userB]
        });
        var sentMessages = new List<(string GroupName, string Method, object? Arg)>();
        var hubContext = CreateMockHubContext(sentMessages);
        var notifier = new SignalRTelemetryNotifier(hubContext, resolver, NullLogger<SignalRTelemetryNotifier>.Instance);

        // Act
        await notifier.NotifyTelemetryEventAsync(
            "864035053671278",
            "ACC_ON",
            -2.455,
            -76.591,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert — should send to both user groups
        Assert.Equal(2, sentMessages.Count);
        Assert.Contains(sentMessages, m => m.GroupName == $"user_{userA}");
        Assert.Contains(sentMessages, m => m.GroupName == $"user_{userB}");
        Assert.All(sentMessages, m => Assert.Equal("TelemetryEventOccurred", m.Method));
    }

    // ──── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubOwnershipResolver : IDeviceOwnershipResolver
    {
        private readonly Dictionary<string, IReadOnlyList<Guid>> _map;

        public StubOwnershipResolver(Dictionary<string, IReadOnlyList<Guid>> map) => _map = map;

        public Task<IReadOnlyList<Guid>> GetUserIdsForImeiAsync(string imei, CancellationToken cancellationToken)
        {
            IReadOnlyList<Guid> result = _map.TryGetValue(imei, out IReadOnlyList<Guid>? ids)
                ? ids
                : [];
            return Task.FromResult(result);
        }
    }

    private static IHubContext<TelemetryHub> CreateMockHubContext(
        List<(string GroupName, string Method, object? Arg)> captured)
    {
        return new FakeHubContext(captured);
    }

    private sealed class FakeHubContext : IHubContext<TelemetryHub>
    {
        private readonly List<(string GroupName, string Method, object? Arg)> _captured;

        public FakeHubContext(List<(string GroupName, string Method, object? Arg)> captured)
        {
            _captured = captured;
            Clients = new FakeHubClients(captured);
            Groups = null!; // Not used in notifier
        }

        public IHubClients Clients { get; }
        public IGroupManager Groups { get; }
    }

    private sealed class FakeHubClients : IHubClients
    {
        private readonly List<(string GroupName, string Method, object? Arg)> _captured;

        public FakeHubClients(List<(string GroupName, string Method, object? Arg)> captured)
        {
            _captured = captured;
        }

        public IClientProxy All => throw new NotImplementedException();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds)
            => throw new NotImplementedException();

        public IClientProxy Client(string connectionId)
            => throw new NotImplementedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds)
            => throw new NotImplementedException();

        public IClientProxy Group(string groupName)
            => new FakeClientProxy(groupName, _captured);

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds)
            => throw new NotImplementedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames)
            => throw new NotImplementedException();

        public IClientProxy User(string userId)
            => throw new NotImplementedException();

        public IClientProxy Users(IReadOnlyList<string> userIds)
            => throw new NotImplementedException();
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        private readonly string _groupName;
        private readonly List<(string GroupName, string Method, object? Arg)> _captured;

        public FakeClientProxy(string groupName, List<(string GroupName, string Method, object? Arg)> captured)
        {
            _groupName = groupName;
            _captured = captured;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _captured.Add((_groupName, method, args.Length > 0 ? args[0] : null));
            return Task.CompletedTask;
        }
    }
}
