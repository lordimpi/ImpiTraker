using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.InMemory;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using TcpServer;

namespace ImpiTrack.Tests;

public sealed class ApiOpsAuthTests
{
    private const string Issuer = "ImpiTrack";
    private const string Audience = "ImpiTrack.Api";
    private const string SigningKey = "imptrack-dev-signing-key-change-me-2026";

    [Fact]
    public async Task Health_ShouldReturnOkWithoutAuth()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpsEndpoint_ShouldReturnUnauthorizedWithoutToken()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/ops/ingestion/ports");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument json = await ReadJsonAsync(response);
        AssertErrorCode(json, "unauthorized");
    }

    [Fact]
    public async Task OpsEndpoint_ShouldReturnForbidden_WhenTokenHasNoAdminRole()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        string token = CreateToken(includeAdminRole: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/ops/ingestion/ports");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument json = await ReadJsonAsync(response);
        AssertErrorCode(json, "forbidden");
    }

    [Fact]
    public async Task OpsEndpoint_ShouldReturnOkWithAdminToken()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        string token = CreateToken(includeAdminRole: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/ops/ingestion/ports");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpsRawLatest_ShouldExposeFailedTrackingParseDetails()
    {
        RawPacketRecord rawRecord = new(
            SessionId.New(),
            PacketId.New(),
            5001,
            "127.0.0.1",
            ProtocolId.Coban,
            "359586015829802",
            MessageType.Tracking,
            "imei:359586015829802,tracker,250208125816,,F,175816.000,A,028,N,07634.01441,W,,;",
            DateTimeOffset.UtcNow,
            RawParseStatus.Failed,
            "invalid_latitude",
            true,
            "ON\r\n",
            DateTimeOffset.UtcNow,
            1.2d);

        await using var factory = CreateFactory([rawRecord]);
        using HttpClient client = factory.CreateClient();
        string token = CreateToken(includeAdminRole: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/ops/raw/latest?imei=359586015829802&limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await ReadJsonAsync(response);
        JsonElement item = json.RootElement.GetProperty("data")[0];
        Assert.Equal((int)MessageType.Tracking, item.GetProperty("messageType").GetInt32());
        Assert.Equal((int)RawParseStatus.Failed, item.GetProperty("parseStatus").GetInt32());
        Assert.Equal("invalid_latitude", item.GetProperty("parseError").GetString());
        Assert.True(item.GetProperty("ackSent").GetBoolean());
    }

    private static WebApplicationFactory<Program> CreateFactory(IReadOnlyList<RawPacketRecord>? rawPackets = null)
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
                    ["IdentityBootstrap:SeedAdminOnStart"] = "false",
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
                TestHostedServiceHelper.RemoveTcpHostedServices(services);
                services.RemoveAll<IOpsRepository>();
                services.RemoveAll<IIngestionRepository>();
                var store = new InMemoryOpsDataStore();
                IReadOnlyList<RawPacketRecord> seedPackets = rawPackets ??
                [
                    new RawPacketRecord(
                        SessionId.New(),
                        PacketId.New(),
                        5001,
                        "127.0.0.1",
                        ProtocolId.Coban,
                        "359586015829802",
                        MessageType.Login,
                        "##,imei:359586015829802,A;",
                        DateTimeOffset.UtcNow,
                        RawParseStatus.Ok,
                        null,
                        true,
                        "LOAD",
                        DateTimeOffset.UtcNow,
                        5)
                ];

                foreach (RawPacketRecord packet in seedPackets)
                {
                    store.AddRawPacket(packet, backlog: 0);
                }

                var repository = new InMemoryDataRepository(store);
                services.AddSingleton<IOpsRepository>(repository);
                services.AddSingleton<IIngestionRepository>(repository);
            });
        });
    }

    private static string CreateToken(bool includeAdminRole)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        byte[] key = Encoding.UTF8.GetBytes(SigningKey);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "ops-user"),
            new(ClaimTypes.Name, "ops-user")
        };

        if (includeAdminRole)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        SecurityToken token = tokenHandler.CreateToken(descriptor);
        return tokenHandler.WriteToken(token);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    private static void AssertErrorCode(JsonDocument json, string expectedCode)
    {
        JsonElement root = json.RootElement;

        if (TryGetProperty(root, "error", out JsonElement errorElement) &&
            TryGetProperty(errorElement, "code", out JsonElement codeElement))
        {
            Assert.Equal(expectedCode, codeElement.GetString());
            Assert.True(TryGetProperty(root, "traceId", out _));
            return;
        }

        if (TryGetProperty(root, "errorCode", out JsonElement legacyCodeElement))
        {
            Assert.Equal(expectedCode, legacyCodeElement.GetString());
            Assert.True(TryGetProperty(root, "traceId", out _));
            return;
        }

        Assert.Fail($"No se encontro error.code ni errorCode en respuesta: {root}");
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        string pascal = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        return element.TryGetProperty(pascal, out value);
    }
}
