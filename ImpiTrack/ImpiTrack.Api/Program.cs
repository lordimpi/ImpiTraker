using System.Text;
using ImpiTrack.Api.Http;
using ImpiTrack.Application.Extensions;
using ImpiTrack.Auth.Infrastructure.Configuration;
using ImpiTrack.Auth.Infrastructure.Extensions;
using ImpiTrack.Auth.Infrastructure.Identity;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Connection;
using ImpiTrack.DataAccess.Extensions;
using ImpiTrack.DataAccess.IOptionPattern;
using ImpiTrack.Shared.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddImpiTrackOptionsCore();
builder.Services.BindOptions<JwtAuthOptions>(builder.Configuration, JwtAuthOptions.SectionName);
builder.Services.BindOptions<IdentityStorageOptions>(builder.Configuration, IdentityStorageOptions.SectionName);
builder.Services.BindOptions<IdentityBootstrapOptions>(builder.Configuration, IdentityBootstrapOptions.SectionName);
builder.Services.BindOptions<EmailOptions>(builder.Configuration, EmailOptions.SectionName);
builder.Services.BindOptions<EmailDispatchOptions>(builder.Configuration, EmailDispatchOptions.SectionName);
builder.Services.AddImpiTrackDataAccess(builder.Configuration, registerMigrationHostedService: false);
builder.Services.AddImpiTrackApplication();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value is not null && x.Value.Errors.Count > 0)
            .ToDictionary(
                x => x.Key,
                x => x.Value!.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "validation_error" : e.ErrorMessage).ToArray());

        ApiResponse<object?> payload = ApiResponseFactory.Failure<object?>(
            "validation_failed",
            "Uno o mas campos no cumplen las validaciones requeridas.",
            context.HttpContext.TraceIdentifier,
            errors);

        return new BadRequestObjectResult(payload);
    };
});

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
string identityInMemoryDatabaseName = builder.Environment.IsEnvironment("Testing")
    ? $"ImpiTrack.Identity.{Guid.NewGuid():N}"
    : "ImpiTrack.Identity";

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

    options.UseInMemoryDatabase(identityInMemoryDatabaseName);
});

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
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

builder.Services.AddImpiTrackAuthInfrastructure();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "ImpiTrack WEB API",
            Version = "v1",
            Description = "Documentacion oficial de la API"
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

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ApiProblemDetailsFactory.WriteErrorAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "unauthorized",
                    "No fue posible autenticar la solicitud.");
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ApiProblemDetailsFactory.WriteErrorAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "forbidden",
                    "No tienes permisos para realizar esta operacion.");
            }
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

if (app.Environment.IsDevelopment() && useIdentitySqlServer)
{
    using IServiceScope migrationScope = app.Services.CreateScope();
    IdentityAppDbContext dbContext = migrationScope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
    await dbContext.Database.MigrateAsync();
}

using (IServiceScope dataAccessMigrationScope = app.Services.CreateScope())
{
    DatabaseRuntimeContext runtimeContext =
        dataAccessMigrationScope.ServiceProvider.GetRequiredService<DatabaseRuntimeContext>();

    if (runtimeContext.IsSqlEnabled && runtimeContext.EnableAutoMigrate)
    {
        ILogger dataAccessMigrationLogger = dataAccessMigrationScope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DataAccessMigrationStartup");

        dataAccessMigrationLogger.LogInformation(
            "db_migrations_start provider={provider}",
            runtimeContext.Provider);

        IMigrationRunner migrationRunner =
            dataAccessMigrationScope.ServiceProvider.GetRequiredService<IMigrationRunner>();

        await migrationRunner.ApplyMigrationsAsync(CancellationToken.None);

        dataAccessMigrationLogger.LogInformation(
            "db_migrations_completed provider={provider}",
            runtimeContext.Provider);
    }
}

using (IServiceScope emailOptionsScope = app.Services.CreateScope())
{
    ILogger emailStartupLogger = emailOptionsScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("EmailConfigurationStartup");

    EmailOptions emailOptions = emailOptionsScope.ServiceProvider
        .GetRequiredService<IGenericOptionsService<EmailOptions>>()
        .GetOptions();

    emailStartupLogger.LogInformation(
        "email_config enabled={enabled} host={host} port={port} useSsl={useSsl} verifyUrl={verifyUrl}",
        emailOptions.Enabled,
        emailOptions.Smtp.Host,
        emailOptions.Smtp.Port,
        emailOptions.Smtp.UseSsl,
        emailOptions.VerifyEmailBaseUrl);
}

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

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseStatusCodePages(async statusCodeContext =>
{
    HttpContext context = statusCodeContext.HttpContext;
    if (context.Response.HasStarted || !string.IsNullOrWhiteSpace(context.Response.ContentType))
    {
        return;
    }

    int statusCode = context.Response.StatusCode;
    if (statusCode < 400)
    {
        return;
    }

    string message = statusCode switch
    {
        StatusCodes.Status401Unauthorized => "No fue posible autenticar la solicitud.",
        StatusCodes.Status403Forbidden => "No tienes permisos para realizar esta operacion.",
        StatusCodes.Status404NotFound => "No existe un recurso para la ruta solicitada.",
        StatusCodes.Status405MethodNotAllowed => "El metodo HTTP no esta permitido para este recurso.",
        _ => "La solicitud no pudo ser procesada."
    };

    await ApiProblemDetailsFactory.WriteErrorAsync(
        context,
        statusCode,
        ApiProblemDetailsFactory.GetDefaultErrorCode(statusCode),
        message);
});

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
