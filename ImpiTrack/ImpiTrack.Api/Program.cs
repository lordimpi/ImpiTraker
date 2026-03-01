using ImpiTrack.Api.Auth.Contracts;
using ImpiTrack.Api.Auth.Services;
using ImpiTrack.Api.Configuration;
using ImpiTrack.Api.Identity;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Extensions;
using ImpiTrack.DataAccess.IOptionPattern;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddImpiTrackOptionsCore();
builder.Services.BindOptions<JwtAuthOptions>(builder.Configuration, JwtAuthOptions.SectionName);
builder.Services.BindOptions<IdentityStorageOptions>(builder.Configuration, IdentityStorageOptions.SectionName);
builder.Services.BindOptions<IdentityBootstrapOptions>(builder.Configuration, IdentityBootstrapOptions.SectionName);
builder.Services.AddImpiTrackDataAccess(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

IdentityStorageOptions identityStorage = builder.Configuration
    .GetSection(IdentityStorageOptions.SectionName)
    .Get<IdentityStorageOptions>() ?? new IdentityStorageOptions();

DatabaseOptions database = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();

string? identityConnection =
    !string.IsNullOrWhiteSpace(identityStorage.ConnectionString)
        ? identityStorage.ConnectionString
        : database.ConnectionString;

bool useIdentitySqlServer =
    string.Equals(identityStorage.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<IdentityAppDbContext>(options =>
{
    if (useIdentitySqlServer)
    {
        if (string.IsNullOrWhiteSpace(identityConnection))
        {
            throw new InvalidOperationException("identity_connection_string_missing");
        }

        options.UseSqlServer(identityConnection, sql =>
        {
            sql.CommandTimeout(Math.Max(1, database.CommandTimeoutSeconds));
        });

        return;
    }

    options.UseInMemoryDatabase("ImpiTrack.Identity");
});

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<IdentityAppDbContext>()
    .AddSignInManager<SignInManager<ApplicationUser>>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IAuthTokenService, AuthTokenService>();
builder.Services.AddHostedService<IdentityBootstrapHostedService>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "ImpiTrack WEB API",
            Version = "v1",
            Description = "Documentación oficial de la API"
        };

        return Task.CompletedTask;
    });

    options.AddDocumentTransformer((document, context, ct) =>
    {
        foreach (var path in document.Paths)
        {
            foreach (var op in path.Value.Operations!)
            {
                var requestBody = op.Value.RequestBody;
                if (requestBody == null)
                {
                    continue;
                }

                if (!requestBody.Content!.ContainsKey("application/json"))
                {
                    continue;
                }

                var jsonSchema = requestBody.Content["application/json"].Schema;
                bool hasFile = jsonSchema!.Properties?
                    .Any(p => p.Value.Format == "binary") ?? false;

                if (!hasFile)
                {
                    continue;
                }

                requestBody.Content.Remove("application/json");
                requestBody.Content["multipart/form-data"] = new()
                {
                    Schema = jsonSchema
                };
            }
        }

        return Task.CompletedTask;
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IGenericOptionsService<JwtAuthOptions>>((options, jwtOptionsService) =>
    {
        JwtAuthOptions jwt = jwtOptionsService.GetOptions();
        byte[] signingKey = Encoding.UTF8.GetBytes(jwt.SigningKey);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin");
    });
});

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.MapScalarApiReference("/scalar/v1", options =>
    {
        options.Title = "ImpiTrack WEB API";
        options.Theme = ScalarTheme.Laserwave;
        options.Layout = ScalarLayout.Modern;
        options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Redirect("/scalar/v1"))
   .ExcludeFromDescription();

app.Run();

/// <summary>
/// Marca parcial para facilitar pruebas de integracion con WebApplicationFactory.
/// </summary>
public partial class Program;
