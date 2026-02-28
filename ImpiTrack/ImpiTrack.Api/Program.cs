using System.Text;
using ImpiTrack.Api.Configuration;
using ImpiTrack.Ops;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtAuthOptions>(builder.Configuration.GetSection(JwtAuthOptions.SectionName));
builder.Services.AddSingleton<IOpsDataStore, InMemoryOpsDataStore>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

JwtAuthOptions jwt = builder.Configuration
    .GetSection(JwtAuthOptions.SectionName)
    .Get<JwtAuthOptions>() ?? new JwtAuthOptions();

byte[] signingKey = Encoding.UTF8.GetBytes(jwt.SigningKey);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

/// <summary>
/// Marca parcial para facilitar pruebas de integracion con WebApplicationFactory.
/// </summary>
public partial class Program;
