using ImpiTrack.Application.Abstractions;
using ImpiTrack.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiTrack.Application.Extensions;

/// <summary>
/// Registro de servicios de la capa Application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra servicios de casos de uso para cuentas y administracion.
    /// </summary>
    /// <param name="services">Coleccion de servicios.</param>
    /// <returns>Coleccion para encadenamiento.</returns>
    public static IServiceCollection AddImpiTrackApplication(this IServiceCollection services)
    {
        services.AddScoped<IMeAccountService, MeAccountService>();
        services.AddScoped<IAdminUsersService, AdminUsersService>();
        return services;
    }
}
