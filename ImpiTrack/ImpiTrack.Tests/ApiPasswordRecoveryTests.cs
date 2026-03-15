using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImpiTrack.Auth.Infrastructure.Email.Contracts;
using ImpiTrack.Auth.Infrastructure.Email.Models;
using ImpiTrack.Ops;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiTrack.Tests;

public sealed class ApiPasswordRecoveryTests
{
    [Fact]
    public async Task ForgotPassword_AndReset_ShouldInvalidateOldRefreshToken_AndAllowNewLogin()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        RegisterResponseData registration = await RegisterAndVerifyAsync(client, "recover.user", "recover.user@imptrack.local");

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "recover.user",
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        ApiEnvelope<AuthTokenPairResponse>? loginPayload = await loginResponse.Content.ReadFromJsonAsync<ApiEnvelope<AuthTokenPairResponse>>();
        Assert.NotNull(loginPayload);
        Assert.NotNull(loginPayload!.Data);

        HttpResponseMessage forgotResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new
        {
            email = registration.Email
        });

        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

        CapturingEmailSender sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        EmailMessage resetMessage = await WaitForEmailAsync(sender, "reset_password", registration.Email);
        (string email, string token) = ExtractResetLink(resetMessage);

        HttpResponseMessage resetResponse = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email,
            token,
            newPassword = "NewChange!456"
        });

        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        HttpResponseMessage refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = loginPayload.Data!.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);

        HttpResponseMessage oldLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = registration.Email,
            password = "ChangeMe!123"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);

        HttpResponseMessage newLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = registration.Email,
            password = "NewChange!456"
        });

        Assert.Equal(HttpStatusCode.OK, newLoginResponse.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ShouldReturnOk_WhenEmailDoesNotExist()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/forgot-password", new
        {
            email = "missing.user@imptrack.local"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CapturingEmailSender sender = factory.Services.GetRequiredService<CapturingEmailSender>();
        await Task.Delay(300);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task ResetPassword_ShouldReturnBadRequest_WhenTokenIsInvalid()
    {
        await using var factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        RegisterResponseData registration = await RegisterAndVerifyAsync(client, "invalid.reset", "invalid.reset@imptrack.local");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = registration.Email,
            token = "bad-token",
            newPassword = "NewChange!456"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using JsonDocument body = await ReadJsonAsync(response);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_password_reset_token", body.RootElement.GetProperty("error").GetProperty("code").GetString());
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
                    ["IdentityBootstrap:SeedAdminOnStart"] = "false",
                    ["IdentityBootstrap:AdminUserName"] = "admin",
                    ["IdentityBootstrap:AdminEmail"] = "admin@imptrack.local",
                    ["IdentityBootstrap:AdminPassword"] = "ChangeMe!123",
                    ["IdentityBootstrap:AdminRole"] = "Admin",
                    ["IdentityBootstrap:UserRole"] = "User",
                    ["Database:Provider"] = "InMemory",
                    ["Database:ConnectionString"] = string.Empty,
                    ["Database:EnableAutoMigrate"] = "false",
                    ["Email:Enabled"] = "false",
                    ["Email:VerifyEmailBaseUrl"] = "https://localhost:5001/api/auth/verify-email/confirm",
                    ["Email:ResetPasswordBaseUrl"] = "https://localhost:4200/auth/reset-password"
                };

                configBuilder.AddInMemoryCollection(data);
            });

            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IOpsDataStore>();
                services.AddSingleton<IOpsDataStore, InMemoryOpsDataStore>();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<CapturingEmailSender>();
                services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
            });
        });
    }

    private static async Task<RegisterResponseData> RegisterAndVerifyAsync(HttpClient client, string userName, string email)
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
        return registerPayload.Data.Registration!;
    }

    private static async Task<EmailMessage> WaitForEmailAsync(CapturingEmailSender sender, string templateName, string to)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            EmailMessage? match = sender.Messages.LastOrDefault(x =>
                string.Equals(x.TemplateName, templateName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.To, to, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"No se encontro correo template={templateName} to={to}");
    }

    private static (string Email, string Token) ExtractResetLink(EmailMessage message)
    {
        Match match = Regex.Match(message.TextBody, @"https?://\S+", RegexOptions.IgnoreCase);
        Assert.True(match.Success);

        Uri uri = new(match.Value.Trim());
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query = QueryHelpers.ParseQuery(uri.Query);

        Assert.True(query.TryGetValue("email", out var emailValues));
        Assert.True(query.TryGetValue("token", out var tokenValues));

        return (Uri.UnescapeDataString(emailValues.ToString()), Uri.UnescapeDataString(tokenValues.ToString()));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
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

    public sealed class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> _messages = new();

        public IReadOnlyCollection<EmailMessage> Messages => _messages.ToArray();

        public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            _messages.Enqueue(message);
            return Task.FromResult(true);
        }
    }
}
