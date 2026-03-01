using System.Net;
using System.Net.Http.Json;
using ImpiTrack.Ops;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiTrack.Tests;

public sealed class ApiAuthFlowTests
{
    [Fact]
    public async Task Login_ShouldReturnTokenPair_WhenSeedAdminConfigured()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "admin",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AuthTokenPairResponse? payload = await response.Content.ReadFromJsonAsync<AuthTokenPairResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(payload.RefreshToken));
    }

    [Fact]
    public async Task Refresh_ShouldRotateToken_WhenRefreshTokenIsValid()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "admin",
            password = "ChangeMe!123"
        });

        AuthTokenPairResponse? loginPayload = await loginResponse.Content.ReadFromJsonAsync<AuthTokenPairResponse>();
        Assert.NotNull(loginPayload);

        HttpResponseMessage refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = loginPayload!.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        AuthTokenPairResponse? refreshPayload = await refreshResponse.Content.ReadFromJsonAsync<AuthTokenPairResponse>();
        Assert.NotNull(refreshPayload);
        Assert.NotEqual(loginPayload.RefreshToken, refreshPayload!.RefreshToken);
    }

    [Fact]
    public async Task OpsEndpoint_ShouldReturnOk_WithAccessTokenFromLogin()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "admin",
            password = "ChangeMe!123"
        });

        AuthTokenPairResponse? loginPayload = await loginResponse.Content.ReadFromJsonAsync<AuthTokenPairResponse>();
        Assert.NotNull(loginPayload);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            loginPayload!.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/ops/ingestion/ports");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((context, configBuilder) =>
            {
                var data = new Dictionary<string, string?>
                {
                    ["IdentityStorage:Provider"] = "InMemory",
                    ["IdentityStorage:ConnectionString"] = string.Empty,
                    ["IdentityBootstrap:SeedAdminOnStart"] = "true",
                    ["IdentityBootstrap:AdminUserName"] = "admin",
                    ["IdentityBootstrap:AdminEmail"] = "admin@imptrack.local",
                    ["IdentityBootstrap:AdminPassword"] = "ChangeMe!123",
                    ["IdentityBootstrap:AdminRole"] = "Admin",
                    ["Database:Provider"] = "InMemory",
                    ["Database:ConnectionString"] = string.Empty,
                    ["Database:EnableAutoMigrate"] = "false"
                };

                configBuilder.AddInMemoryCollection(data);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOpsDataStore>();
                services.AddSingleton<IOpsDataStore, InMemoryOpsDataStore>();
            });
        });
    }

    private sealed class AuthTokenPairResponse
    {
        public string AccessToken { get; set; } = string.Empty;

        public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }

        public string RefreshToken { get; set; } = string.Empty;

        public DateTimeOffset RefreshTokenExpiresAtUtc { get; set; }
    }
}
