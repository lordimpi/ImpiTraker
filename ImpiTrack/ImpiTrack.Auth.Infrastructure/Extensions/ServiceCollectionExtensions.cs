using ImpiTrack.Application.Abstractions;
using ImpiTrack.Auth.Infrastructure.Auth.Contracts;
using ImpiTrack.Auth.Infrastructure.Auth.Services;
using ImpiTrack.Auth.Infrastructure.Email.Contracts;
using ImpiTrack.Auth.Infrastructure.Email.Services;
using ImpiTrack.Auth.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiTrack.Auth.Infrastructure.Extensions;

/// <summary>
/// Registro de servicios de autenticacion e identidad.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra servicios de autenticacion de la infraestructura.
    /// </summary>
    /// <param name="services">Coleccion de servicios.</param>
    /// <returns>Coleccion para encadenamiento.</returns>
    public static IServiceCollection AddImpiTrackAuthInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IAuthTokenService, AuthTokenService>();
        services.AddScoped<IIdentityUserLookup, IdentityUserLookup>();
        services.AddSingleton<IEmailDispatchQueue, ChannelEmailDispatchQueue>();
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddHostedService<EmailDispatchBackgroundService>();
        services.AddHostedService<IdentityBootstrapHostedService>();
        return services;
    }
}
