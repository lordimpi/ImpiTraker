using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Queue;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TcpServer;

namespace ImpiTrack.Tests;

public sealed class ApiTelemetryTests
{
    [Fact]
    public async Task MeTelemetryDevices_ShouldReturnSummaryWithLastPosition()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        AuthTokenPairResponse token = await RegisterVerifyLoginAndBindAsync(
            client,
            "telemetry.user",
            "telemetry.user@imptrack.local",
            "359586015829802");

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        await SeedSessionAsync(factory, "359586015829802", nowUtc.AddMinutes(-10), nowUtc);
        await SeedTrackingAsync(
            factory,
            "359586015829802",
            nowUtc.AddMinutes(-2),
            nowUtc.AddMinutes(-1),
            4.7110,
            -74.0721,
            42,
            180);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/me/telemetry/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ApiEnvelope<List<TelemetryDeviceSummaryResponse>>? payload =
            await response.Content.ReadFromJsonAsync<ApiEnvelope<List<TelemetryDeviceSummaryResponse>>>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.NotNull(payload.Data);
        TelemetryDeviceSummaryResponse device = Assert.Single(payload.Data!);
        Assert.Equal("359586015829802", device.Imei);
        Assert.NotNull(device.LastSeenAtUtc);
        Assert.NotNull(device.ActiveSessionId);
        Assert.Equal((int)ProtocolId.Coban, device.Protocol);
        Assert.Equal((int)MessageType.Tracking, device.LastMessageType);
        Assert.NotNull(device.LastPosition);
        Assert.Equal(4.7110, device.LastPosition!.Latitude, 3);
        Assert.Equal(-74.0721, device.LastPosition.Longitude, 3);
    }

    [Fact]
    public async Task MeTelemetryDevices_ShouldReturnAliasAsNull_ForExistingBinding()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        AuthTokenPairResponse token = await RegisterVerifyLoginAndBindAsync(
            client,
            "alias.null.user",
            "alias.null.user@imptrack.local",
            "359586015829803");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/me/telemetry/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ApiEnvelope<List<TelemetryDeviceSummaryResponse>>? payload =
            await response.Content.ReadFromJsonAsync<ApiEnvelope<List<TelemetryDeviceSummaryResponse>>>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.NotNull(payload.Data);
        TelemetryDeviceSummaryResponse device = Assert.Single(payload.Data!);
        Assert.Equal("359586015829803", device.Imei);
        Assert.Null(device.Alias);
    }

    [Fact]
    public async Task MeTelemetryPositions_ShouldRespectWindowAndLimit()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        AuthTokenPairResponse token = await RegisterVerifyLoginAndBindAsync(
            client,
            "positions.user",
            "positions.user@imptrack.local",
            "111222333444555");

        DateTimeOffset baseUtc = DateTimeOffset.UtcNow.AddHours(-1);
        await SeedTrackingAsync(factory, "111222333444555", baseUtc.AddMinutes(5), baseUtc.AddMinutes(5), 4.6001, -74.1001, 30, 90);
        await SeedTrackingAsync(factory, "111222333444555", baseUtc.AddMinutes(15), baseUtc.AddMinutes(15), 4.7001, -74.0001, 50, 120);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        string from = Uri.EscapeDataString(baseUtc.ToString("O"));
        string to = Uri.EscapeDataString(baseUtc.AddMinutes(20).ToString("O"));
        HttpResponseMessage response = await client.GetAsync(
            $"/api/me/telemetry/devices/111222333444555/positions?from={from}&to={to}&limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ApiEnvelope<List<DevicePositionPointResponse>>? payload =
            await response.Content.ReadFromJsonAsync<ApiEnvelope<List<DevicePositionPointResponse>>>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.NotNull(payload.Data);
        DevicePositionPointResponse item = Assert.Single(payload.Data!);
        Assert.Equal(4.7001, item.Latitude, 3);
        Assert.Equal(-74.0001, item.Longitude, 3);
    }

    [Fact]
    public async Task MeTelemetryEvents_ShouldReturnOrderedEvents()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        AuthTokenPairResponse token = await RegisterVerifyLoginAndBindAsync(
            client,
            "events.user",
            "events.user@imptrack.local",
            "998877665544332");

        DateTimeOffset baseUtc = DateTimeOffset.UtcNow.AddHours(-1);
        await SeedEventAsync(factory, "998877665544332", MessageType.Login, "login-payload", baseUtc.AddMinutes(1));
        await SeedEventAsync(factory, "998877665544332", MessageType.Heartbeat, "heartbeat-payload", baseUtc.AddMinutes(5));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/me/telemetry/devices/998877665544332/events?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ApiEnvelope<List<DeviceEventResponse>>? payload =
            await response.Content.ReadFromJsonAsync<ApiEnvelope<List<DeviceEventResponse>>>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal(2, payload.Data!.Count);
        Assert.Equal("Heartbeat", payload.Data[0].EventCode);
        Assert.Equal("heartbeat-payload", payload.Data[0].PayloadText);
        Assert.True(payload.Data[0].ReceivedAtUtc >= payload.Data[1].ReceivedAtUtc);
    }

    [Fact]
    public async Task MeTelemetry_ShouldReturnNotFound_WhenImeiIsNotOwned()
    {
        await using var factory = CreateFactory();
        using HttpClient ownerClient = factory.CreateClient();
        using HttpClient otherClient = factory.CreateClient();

        await RegisterVerifyLoginAndBindAsync(
            ownerClient,
            "owner.telemetry",
            "owner.telemetry@imptrack.local",
            "123123123123123");

        AuthTokenPairResponse otherToken = await RegisterVerifyAndLoginAsync(
            otherClient,
            "other.telemetry",
            "other.telemetry@imptrack.local");

        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken.AccessToken);

        HttpResponseMessage response = await otherClient.GetAsync("/api/me/telemetry/devices/123123123123123/positions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = await ReadJsonAsync(response);
        Assert.Equal("device_binding_not_found", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdminTelemetry_ShouldUseUserContext()
    {
        await using var factory = CreateFactory(seedAdminOnStart: true);
        using HttpClient adminClient = factory.CreateClient();

        Guid targetUserId = await RegisterVerifyAndBindAsync(
            factory.CreateClient(),
            "admin.telemetry.target",
            "admin.telemetry.target@imptrack.local",
            "555666777888999");

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        await SeedTrackingAsync(
            factory,
            "555666777888999",
            nowUtc.AddMinutes(-3),
            nowUtc.AddMinutes(-2),
            6.2518,
            -75.5636,
            55,
            270);

        AuthTokenPairResponse adminToken = await LoginAsAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken.AccessToken);

        HttpResponseMessage successResponse = await adminClient.GetAsync($"/api/admin/users/{targetUserId:D}/telemetry/devices");
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
        ApiEnvelope<List<TelemetryDeviceSummaryResponse>>? successPayload =
            await successResponse.Content.ReadFromJsonAsync<ApiEnvelope<List<TelemetryDeviceSummaryResponse>>>();
        Assert.NotNull(successPayload);
        Assert.NotNull(successPayload!.Data);
        Assert.Single(successPayload.Data!);

        HttpResponseMessage wrongUserResponse = await adminClient.GetAsync($"/api/admin/users/{Guid.NewGuid():D}/telemetry/devices/555666777888999/positions");
        Assert.Equal(HttpStatusCode.NotFound, wrongUserResponse.StatusCode);
        using JsonDocument wrongUserBody = await ReadJsonAsync(wrongUserResponse);
        Assert.Equal("user_not_found", wrongUserBody.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MeTelemetryTrips_ShouldReturnSegmentedTripsAndDetail()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        AuthTokenPairResponse token = await RegisterVerifyLoginAndBindAsync(
            client,
            "trips.user",
            "trips.user@imptrack.local",
            "300100200300400");

        DateTimeOffset baseUtc = DateTimeOffset.UtcNow.AddHours(-3);
        await SeedTrackingAsync(factory, "300100200300400", baseUtc.AddMinutes(0), baseUtc.AddMinutes(0), 4.6000, -74.1000, 12, 0);
        await SeedTrackingAsync(factory, "300100200300400", baseUtc.AddMinutes(3), baseUtc.AddMinutes(3), 4.6200, -74.1200, 18, 45);
        await SeedTrackingAsync(factory, "300100200300400", baseUtc.AddMinutes(20), baseUtc.AddMinutes(20), 4.7000, -74.0000, 22, 90);
        await SeedTrackingAsync(factory, "300100200300400", baseUtc.AddMinutes(24), baseUtc.AddMinutes(24), 4.7400, -73.9600, 28, 120);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        string from = Uri.EscapeDataString(baseUtc.AddMinutes(-5).ToString("O"));
        string to = Uri.EscapeDataString(baseUtc.AddMinutes(40).ToString("O"));
        HttpResponseMessage tripsResponse = await client.GetAsync(
            $"/api/me/telemetry/devices/300100200300400/trips?from={from}&to={to}&limit=10");

        Assert.Equal(HttpStatusCode.OK, tripsResponse.StatusCode);
        ApiEnvelope<List<TripSummaryResponse>>? tripsPayload =
            await tripsResponse.Content.ReadFromJsonAsync<ApiEnvelope<List<TripSummaryResponse>>>();
        Assert.NotNull(tripsPayload);
        Assert.True(tripsPayload!.Success);
        Assert.NotNull(tripsPayload.Data);
        Assert.Equal(2, tripsPayload.Data!.Count);
        Assert.True(tripsPayload.Data[0].StartedAtUtc > tripsPayload.Data[1].StartedAtUtc);
        Assert.All(tripsPayload.Data, trip => Assert.True(trip.PointCount >= 2));

        string tripId = tripsPayload.Data[0].TripId;
        HttpResponseMessage detailResponse = await client.GetAsync(
            $"/api/me/telemetry/devices/300100200300400/trips/{tripId}?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        ApiEnvelope<TripDetailResponse>? detailPayload =
            await detailResponse.Content.ReadFromJsonAsync<ApiEnvelope<TripDetailResponse>>();
        Assert.NotNull(detailPayload);
        Assert.True(detailPayload!.Success);
        Assert.NotNull(detailPayload.Data);
        Assert.Equal(tripId, detailPayload.Data!.TripId);
        Assert.Equal("movement_2d_acc_v2", detailPayload.Data.SourceRule);
        Assert.True(detailPayload.Data.PathPoints.Count >= 2);
    }

    [Fact]
    public async Task MeTelemetryTrips_ShouldReturnOpenTrip_WhenTripIsInProgress()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        AuthTokenPairResponse token = await RegisterVerifyLoginAndBindAsync(
            client,
            "trips.open.user",
            "trips.open.user@imptrack.local",
            "300100200300401");

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        await SeedTrackingAsync(factory, "300100200300401", nowUtc.AddMinutes(-8), nowUtc.AddMinutes(-8), 4.5000, -74.0500, 25, 0);
        await SeedTrackingAsync(factory, "300100200300401", nowUtc.AddMinutes(-3), nowUtc.AddMinutes(-3), 4.5200, -74.0300, 30, 25);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/me/telemetry/devices/300100200300401/trips?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ApiEnvelope<List<TripSummaryResponse>>? payload =
            await response.Content.ReadFromJsonAsync<ApiEnvelope<List<TripSummaryResponse>>>();
        Assert.NotNull(payload);
        TripSummaryResponse trip = Assert.Single(payload!.Data!);
        Assert.Null(trip.EndedAtUtc);
    }

    [Fact]
    public async Task MeTelemetryTripDetail_ShouldReturnNotFound_WhenTripDoesNotExist()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        AuthTokenPairResponse token = await RegisterVerifyLoginAndBindAsync(
            client,
            "trips.missing.user",
            "trips.missing.user@imptrack.local",
            "300100200300402");

        DateTimeOffset baseUtc = DateTimeOffset.UtcNow.AddHours(-2);
        await SeedTrackingAsync(factory, "300100200300402", baseUtc, baseUtc, 4.6000, -74.1000, 12, 0);
        await SeedTrackingAsync(factory, "300100200300402", baseUtc.AddMinutes(2), baseUtc.AddMinutes(2), 4.6100, -74.1100, 15, 10);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/me/telemetry/devices/300100200300402/trips/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = await ReadJsonAsync(response);
        Assert.Equal("trip_not_found", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdminTelemetryTrips_ShouldUseUserContext()
    {
        await using var factory = CreateFactory(seedAdminOnStart: true);
        using HttpClient targetClient = factory.CreateClient();
        using HttpClient adminClient = factory.CreateClient();

        Guid targetUserId = await RegisterVerifyAndBindAsync(
            targetClient,
            "admin.trips.target",
            "admin.trips.target@imptrack.local",
            "300100200300403");

        DateTimeOffset baseUtc = DateTimeOffset.UtcNow.AddHours(-1);
        await SeedTrackingAsync(factory, "300100200300403", baseUtc, baseUtc, 6.2000, -75.5000, 20, 0);
        await SeedTrackingAsync(factory, "300100200300403", baseUtc.AddMinutes(3), baseUtc.AddMinutes(3), 6.2100, -75.4900, 24, 15);

        AuthTokenPairResponse adminToken = await LoginAsAdminAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken.AccessToken);

        HttpResponseMessage response = await adminClient.GetAsync($"/api/admin/users/{targetUserId:D}/telemetry/devices/300100200300403/trips");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ApiEnvelope<List<TripSummaryResponse>>? payload =
            await response.Content.ReadFromJsonAsync<ApiEnvelope<List<TripSummaryResponse>>>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Data!);

        HttpResponseMessage wrongUserResponse = await adminClient.GetAsync(
            $"/api/admin/users/{Guid.NewGuid():D}/telemetry/devices/300100200300403/trips");
        Assert.Equal(HttpStatusCode.NotFound, wrongUserResponse.StatusCode);
        using JsonDocument wrongUserBody = await ReadJsonAsync(wrongUserResponse);
        Assert.Equal("user_not_found", wrongUserBody.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        bool seedAdminOnStart = false,
        string environmentName = "Testing")
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environmentName);

            builder.ConfigureAppConfiguration((context, configBuilder) =>
            {
                var data = new Dictionary<string, string?>
                {
                    ["IdentityStorage:Provider"] = "InMemory",
                    ["IdentityStorage:ConnectionString"] = string.Empty,
                    ["IdentityBootstrap:SeedAdminOnStart"] = seedAdminOnStart ? "true" : "false",
                    ["IdentityBootstrap:AdminUserName"] = "admin",
                    ["IdentityBootstrap:AdminEmail"] = "admin@imptrack.local",
                    ["IdentityBootstrap:AdminPassword"] = "ChangeMe!123",
                    ["IdentityBootstrap:AdminRole"] = "Admin",
                    ["IdentityBootstrap:UserRole"] = "User",
                    ["Database:Provider"] = "InMemory",
                    ["Database:ConnectionString"] = string.Empty,
                    ["Database:EnableAutoMigrate"] = "false",
                    ["TcpServerConfig:Servers:0:Name"] = "Disabled",
                    ["TcpServerConfig:Servers:0:Port"] = "0",
                    ["TcpServerConfig:Servers:0:Protocol"] = "COBAN",
                    ["TcpServerConfig:Pipeline:ChannelCapacity"] = "10",
                    ["TcpServerConfig:Pipeline:ConsumerWorkers"] = "1",
                    ["EventBus:Provider"] = "InMemory"
                };

                configBuilder.AddInMemoryCollection(data);
            });

            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IOpsDataStore>();
                services.AddSingleton<IOpsDataStore, InMemoryOpsDataStore>();
                TestHostedServiceHelper.RemoveTcpHostedServices(services);
            });
        });
    }

    private static async Task<AuthTokenPairResponse> RegisterVerifyLoginAndBindAsync(
        HttpClient client,
        string userName,
        string email,
        string imei)
    {
        AuthTokenPairResponse token = await RegisterVerifyAndLoginAsync(client, userName, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        HttpResponseMessage bindResponse = await client.PostAsJsonAsync("/api/me/devices", new
        {
            imei
        });

        Assert.Equal(HttpStatusCode.OK, bindResponse.StatusCode);
        return token;
    }

    private static async Task<Guid> RegisterVerifyAndBindAsync(
        HttpClient client,
        string userName,
        string email,
        string imei)
    {
        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName,
            email,
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        ApiEnvelope<RegisterResultResponse>? registerPayload = await registerResponse.Content.ReadFromJsonAsync<ApiEnvelope<RegisterResultResponse>>();
        Assert.NotNull(registerPayload);
        Assert.NotNull(registerPayload!.Data);
        Assert.NotNull(registerPayload.Data!.Registration);

        HttpResponseMessage verifyResponse = await client.PostAsJsonAsync("/api/auth/verify-email", new
        {
            userId = registerPayload.Data.Registration!.UserId,
            token = registerPayload.Data.Registration.EmailVerificationToken
        });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = userName,
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        ApiEnvelope<AuthTokenPairResponse>? tokenPayload = await loginResponse.Content.ReadFromJsonAsync<ApiEnvelope<AuthTokenPairResponse>>();
        Assert.NotNull(tokenPayload);
        Assert.NotNull(tokenPayload!.Data);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPayload.Data!.AccessToken);

        HttpResponseMessage bindResponse = await client.PostAsJsonAsync("/api/me/devices", new
        {
            imei
        });

        Assert.Equal(HttpStatusCode.OK, bindResponse.StatusCode);
        return registerPayload.Data.Registration.UserId;
    }

    private static async Task<AuthTokenPairResponse> RegisterVerifyAndLoginAsync(HttpClient client, string userName, string email)
    {
        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName,
            email,
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        ApiEnvelope<RegisterResultResponse>? registerPayload = await registerResponse.Content.ReadFromJsonAsync<ApiEnvelope<RegisterResultResponse>>();
        Assert.NotNull(registerPayload);
        Assert.NotNull(registerPayload!.Data);
        Assert.NotNull(registerPayload.Data!.Registration);

        HttpResponseMessage verifyResponse = await client.PostAsJsonAsync("/api/auth/verify-email", new
        {
            userId = registerPayload.Data.Registration!.UserId,
            token = registerPayload.Data.Registration.EmailVerificationToken
        });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = userName,
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        ApiEnvelope<AuthTokenPairResponse>? loginPayload = await loginResponse.Content.ReadFromJsonAsync<ApiEnvelope<AuthTokenPairResponse>>();
        Assert.NotNull(loginPayload);
        Assert.NotNull(loginPayload!.Data);
        return loginPayload.Data!;
    }

    private static async Task<AuthTokenPairResponse> LoginAsAdminAsync(HttpClient client)
    {
        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "admin",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        ApiEnvelope<AuthTokenPairResponse>? loginPayload = await loginResponse.Content.ReadFromJsonAsync<ApiEnvelope<AuthTokenPairResponse>>();
        Assert.NotNull(loginPayload);
        Assert.NotNull(loginPayload!.Data);
        return loginPayload.Data!;
    }

    private static async Task SeedSessionAsync(
        WebApplicationFactory<Program> factory,
        string imei,
        DateTimeOffset connectedAtUtc,
        DateTimeOffset lastSeenAtUtc)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IIngestionRepository ingestionRepository = scope.ServiceProvider.GetRequiredService<IIngestionRepository>();
        await ingestionRepository.UpsertSessionAsync(
            new SessionRecord(
                SessionId.New(),
                "127.0.0.1",
                5001,
                connectedAtUtc,
                lastSeenAtUtc,
                lastSeenAtUtc,
                imei,
                2,
                0,
                null,
                null,
                true),
            CancellationToken.None);
    }

    private static async Task SeedTrackingAsync(
        WebApplicationFactory<Program> factory,
        string imei,
        DateTimeOffset gpsTimeUtc,
        DateTimeOffset receivedAtUtc,
        double latitude,
        double longitude,
        double? speedKmh,
        int? headingDeg)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IIngestionRepository ingestionRepository = scope.ServiceProvider.GetRequiredService<IIngestionRepository>();

        SessionId sessionId = SessionId.New();
        PacketId packetId = PacketId.New();

        await ingestionRepository.UpsertSessionAsync(
            new SessionRecord(
                sessionId,
                "127.0.0.1",
                5001,
                receivedAtUtc.AddMinutes(-1),
                receivedAtUtc,
                null,
                imei,
                1,
                0,
                null,
                null,
                true),
            CancellationToken.None);

        PersistEnvelopeResult result = await ingestionRepository.PersistEnvelopeAsync(
            new InboundEnvelope(
                sessionId,
                packetId,
                5001,
                "127.0.0.1",
                new ParsedMessage(
                    ProtocolId.Coban,
                    MessageType.Tracking,
                    imei,
                    ReadOnlyMemory<byte>.Empty,
                    $"imei:{imei},tracker",
                    receivedAtUtc,
                    gpsTimeUtc,
                    latitude,
                    longitude,
                    speedKmh,
                    headingDeg),
                receivedAtUtc),
            CancellationToken.None);

        Assert.Equal(PersistEnvelopeStatus.Persisted, result.Status);
    }

    private static async Task SeedEventAsync(
        WebApplicationFactory<Program> factory,
        string imei,
        MessageType messageType,
        string payloadText,
        DateTimeOffset receivedAtUtc)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IIngestionRepository ingestionRepository = scope.ServiceProvider.GetRequiredService<IIngestionRepository>();

        SessionId sessionId = SessionId.New();
        PacketId packetId = PacketId.New();

        await ingestionRepository.UpsertSessionAsync(
            new SessionRecord(
                sessionId,
                "127.0.0.1",
                5001,
                receivedAtUtc.AddMinutes(-1),
                receivedAtUtc,
                messageType == MessageType.Heartbeat ? receivedAtUtc : null,
                imei,
                1,
                0,
                null,
                null,
                true),
            CancellationToken.None);

        PersistEnvelopeResult result = await ingestionRepository.PersistEnvelopeAsync(
            new InboundEnvelope(
                sessionId,
                packetId,
                5001,
                "127.0.0.1",
                new ParsedMessage(
                    ProtocolId.Coban,
                    messageType,
                    imei,
                    ReadOnlyMemory<byte>.Empty,
                    payloadText,
                    receivedAtUtc),
                receivedAtUtc),
            CancellationToken.None);

        Assert.Equal(PersistEnvelopeStatus.Persisted, result.Status);
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }

        public T? Data { get; set; }

        public ApiErrorResponse? Error { get; set; }

        public string TraceId { get; set; } = string.Empty;
    }

    private sealed class ApiErrorResponse
    {
        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    private sealed class RegisterResultResponse
    {
        public int Status { get; set; }

        public RegisterResponseData? Registration { get; set; }

        public List<string> Errors { get; set; } = [];
    }

    private sealed class RegisterResponseData
    {
        public Guid UserId { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public bool RequiresEmailVerification { get; set; }

        public string EmailVerificationToken { get; set; } = string.Empty;
    }

    private sealed class AuthTokenPairResponse
    {
        public string AccessToken { get; set; } = string.Empty;

        public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }

        public string RefreshToken { get; set; } = string.Empty;

        public DateTimeOffset RefreshTokenExpiresAtUtc { get; set; }
    }

    private sealed class TelemetryDeviceSummaryResponse
    {
        public string Imei { get; set; } = string.Empty;

        public DateTimeOffset BoundAtUtc { get; set; }

        public DateTimeOffset? LastSeenAtUtc { get; set; }

        public Guid? ActiveSessionId { get; set; }

        public int? Protocol { get; set; }

        public int? LastMessageType { get; set; }

        public LastKnownPositionResponse? LastPosition { get; set; }

        public string? Alias { get; set; }
    }

    private sealed class LastKnownPositionResponse
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

    private sealed class DevicePositionPointResponse
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

    private sealed class DeviceEventResponse
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

    private sealed class TripSummaryResponse
    {
        public string TripId { get; set; } = string.Empty;

        public string Imei { get; set; } = string.Empty;

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset? EndedAtUtc { get; set; }

        public int PointCount { get; set; }

        public double? MaxSpeedKmh { get; set; }

        public double? AvgSpeedKmh { get; set; }

        public DevicePositionPointResponse? StartPosition { get; set; }

        public DevicePositionPointResponse? EndPosition { get; set; }
    }

    private sealed class TripDetailResponse
    {
        public string TripId { get; set; } = string.Empty;

        public string Imei { get; set; } = string.Empty;

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset? EndedAtUtc { get; set; }

        public int PointCount { get; set; }

        public double? MaxSpeedKmh { get; set; }

        public double? AvgSpeedKmh { get; set; }

        public List<DevicePositionPointResponse> PathPoints { get; set; } = [];

        public DevicePositionPointResponse? StartPosition { get; set; }

        public DevicePositionPointResponse? EndPosition { get; set; }

        public string SourceRule { get; set; } = string.Empty;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }
}
