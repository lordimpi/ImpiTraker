using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiTrack.Ops;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiTrack.Tests;

public sealed class ApiRegistrationAndAccountTests
{
    [Fact]
    public async Task Register_ShouldHideVerificationToken_WhenEnvironmentIsProduction()
    {
        await using var factory = CreateFactory(environmentName: "Production");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "prod.user",
            email = "prod.user@imptrack.local",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        ApiEnvelope<RegisterResultResponse>? registerPayload = await registerResponse.Content.ReadFromJsonAsync<ApiEnvelope<RegisterResultResponse>>();
        Assert.NotNull(registerPayload);
        Assert.True(registerPayload!.Success);
        Assert.NotNull(registerPayload.Data);
        Assert.NotNull(registerPayload.Data!.Registration);
        Assert.Equal(string.Empty, registerPayload.Data.Registration!.EmailVerificationToken);
    }

    [Fact]
    public async Task Register_Verify_Login_AndBindDevice_ShouldWork()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "user.test",
            email = "user.test@imptrack.local",
            password = "ChangeMe!123",
            fullName = "Usuario Prueba"
        });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        ApiEnvelope<RegisterResultResponse>? registerPayload = await registerResponse.Content.ReadFromJsonAsync<ApiEnvelope<RegisterResultResponse>>();
        Assert.NotNull(registerPayload);
        Assert.True(registerPayload!.Success);
        Assert.NotNull(registerPayload.Data);
        Assert.NotNull(registerPayload.Data!.Registration);
        Assert.False(string.IsNullOrWhiteSpace(registerPayload.Data.Registration!.EmailVerificationToken));

        HttpResponseMessage loginBeforeVerify = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "user.test",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, loginBeforeVerify.StatusCode);

        HttpResponseMessage verifyResponse = await client.PostAsJsonAsync("/api/auth/verify-email", new
        {
            userId = registerPayload.Data.Registration.UserId,
            token = registerPayload.Data.Registration.EmailVerificationToken
        });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "user.test",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        ApiEnvelope<AuthTokenPairResponse>? loginPayload = await loginResponse.Content.ReadFromJsonAsync<ApiEnvelope<AuthTokenPairResponse>>();
        Assert.NotNull(loginPayload);
        Assert.True(loginPayload!.Success);
        Assert.NotNull(loginPayload.Data);
        Assert.False(string.IsNullOrWhiteSpace(loginPayload.Data!.AccessToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginPayload.Data.AccessToken);

        HttpResponseMessage meResponse = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        ApiEnvelope<MeSummaryResponse>? meSummary = await meResponse.Content.ReadFromJsonAsync<ApiEnvelope<MeSummaryResponse>>();
        Assert.NotNull(meSummary);
        Assert.True(meSummary!.Success);
        Assert.NotNull(meSummary.Data);
        Assert.Equal("BASIC", meSummary.Data!.PlanCode);

        HttpResponseMessage bindResponse = await client.PostAsJsonAsync("/api/me/devices", new
        {
            imei = "359586015829802"
        });

        Assert.Equal(HttpStatusCode.OK, bindResponse.StatusCode);

        HttpResponseMessage devicesResponse = await client.GetAsync("/api/me/devices");
        Assert.Equal(HttpStatusCode.OK, devicesResponse.StatusCode);
        ApiEnvelope<List<UserDeviceResponse>>? devicesPayload = await devicesResponse.Content.ReadFromJsonAsync<ApiEnvelope<List<UserDeviceResponse>>>();
        Assert.NotNull(devicesPayload);
        Assert.True(devicesPayload!.Success);
        Assert.NotNull(devicesPayload.Data);
        Assert.Contains(devicesPayload.Data!, x => x.Imei == "359586015829802");
    }

    [Fact]
    public async Task AdminEndpoints_ShouldSetPlanAndBindDevice_ForUser()
    {
        await using var factory = CreateFactory(seedAdminOnStart: true);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "admin.target",
            email = "admin.target@imptrack.local",
            password = "ChangeMe!123"
        });

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

        HttpResponseMessage adminLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "admin",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.OK, adminLoginResponse.StatusCode);
        ApiEnvelope<AuthTokenPairResponse>? adminToken = await adminLoginResponse.Content.ReadFromJsonAsync<ApiEnvelope<AuthTokenPairResponse>>();
        Assert.NotNull(adminToken);
        Assert.NotNull(adminToken!.Data);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken.Data!.AccessToken);

        HttpResponseMessage setPlanResponse = await client.PutAsJsonAsync(
            $"/api/admin/users/{registerPayload.Data.Registration.UserId}/plan",
            new
            {
                planCode = "PRO"
            });

        Assert.Equal(HttpStatusCode.OK, setPlanResponse.StatusCode);
        ApiEnvelope<MeSummaryResponse>? summaryPayload = await setPlanResponse.Content.ReadFromJsonAsync<ApiEnvelope<MeSummaryResponse>>();
        Assert.NotNull(summaryPayload);
        Assert.True(summaryPayload!.Success);
        Assert.NotNull(summaryPayload.Data);
        Assert.Equal("PRO", summaryPayload.Data!.PlanCode);

        HttpResponseMessage bindResponse = await client.PostAsJsonAsync(
            $"/api/admin/users/{registerPayload.Data.Registration.UserId}/devices",
            new
            {
                imei = "111222333444555"
            });

        Assert.Equal(HttpStatusCode.OK, bindResponse.StatusCode);
    }

    [Fact]
    public async Task Register_ShouldReturnValidationError_WhenModelValidationFails()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "ab",
            email = "not-an-email",
            password = "123"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonAsync(response);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("validation_failed", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task BindDevice_ShouldReturnConflict_WhenImeiBelongsToAnotherUser()
    {
        await using var factory = CreateFactory();
        using HttpClient ownerClient = factory.CreateClient();
        using HttpClient secondClient = factory.CreateClient();

        AuthTokenPairResponse ownerToken = await RegisterVerifyAndLoginAsync(
            ownerClient,
            "owner.user",
            "owner.user@imptrack.local");

        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken.AccessToken);
        HttpResponseMessage ownerBind = await ownerClient.PostAsJsonAsync("/api/me/devices", new
        {
            imei = "998877665544332"
        });
        Assert.Equal(HttpStatusCode.OK, ownerBind.StatusCode);

        AuthTokenPairResponse secondToken = await RegisterVerifyAndLoginAsync(
            secondClient,
            "second.user",
            "second.user@imptrack.local");

        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondToken.AccessToken);
        HttpResponseMessage secondBind = await secondClient.PostAsJsonAsync("/api/me/devices", new
        {
            imei = "998877665544332"
        });

        Assert.Equal(HttpStatusCode.Conflict, secondBind.StatusCode);
        Assert.StartsWith("application/json", secondBind.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonAsync(secondBind);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("imei_owned_by_another_user", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task VerifyEmailConfirmGet_ShouldConfirmAccount_AndAllowLogin()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "link.verify",
            email = "link.verify@imptrack.local",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        ApiEnvelope<RegisterResultResponse>? registerPayload = await registerResponse.Content.ReadFromJsonAsync<ApiEnvelope<RegisterResultResponse>>();
        Assert.NotNull(registerPayload);
        Assert.NotNull(registerPayload!.Data);
        Assert.NotNull(registerPayload.Data!.Registration);
        Assert.False(string.IsNullOrWhiteSpace(registerPayload.Data.Registration!.EmailVerificationToken));

        string encodedToken = Uri.EscapeDataString(registerPayload.Data.Registration.EmailVerificationToken);
        HttpResponseMessage verifyResponse = await client.GetAsync(
            $"/api/auth/verify-email/confirm?userId={registerPayload.Data.Registration.UserId:D}&token={encodedToken}");

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        Assert.StartsWith("text/html", verifyResponse.Content.Headers.ContentType?.MediaType);
        string verifyHtml = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Contains("Correo verificado", verifyHtml, StringComparison.OrdinalIgnoreCase);

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "link.verify",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        ApiEnvelope<AuthTokenPairResponse>? loginPayload = await loginResponse.Content.ReadFromJsonAsync<ApiEnvelope<AuthTokenPairResponse>>();
        Assert.NotNull(loginPayload);
        Assert.NotNull(loginPayload!.Data);
        Assert.False(string.IsNullOrWhiteSpace(loginPayload.Data!.AccessToken));
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
                    ["Database:EnableAutoMigrate"] = "false"
                };

                configBuilder.AddInMemoryCollection(data);
            });

            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IOpsDataStore>();
                services.AddSingleton<IOpsDataStore, InMemoryOpsDataStore>();
            });
        });
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

    private sealed class MeSummaryResponse
    {
        public Guid UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public string PlanCode { get; set; } = string.Empty;

        public string PlanName { get; set; } = string.Empty;

        public int MaxGps { get; set; }

        public int UsedGps { get; set; }
    }

    private sealed class UserDeviceResponse
    {
        public Guid DeviceId { get; set; }

        public string Imei { get; set; } = string.Empty;

        public DateTimeOffset BoundAtUtc { get; set; }
    }

    private static async Task<AuthTokenPairResponse> RegisterVerifyAndLoginAsync(
        HttpClient client,
        string userName,
        string email)
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

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }
}
