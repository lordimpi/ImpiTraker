using ImpiTrack.Application.Abstractions;
using ImpiTrack.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiTrack.Application.Extensions;

/// <summary>
/// Registro de servicios de la capa Application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra servicios de casos de uso para cuentas, administracion y comandos de dispositivo.
    /// </summary>
    /// <param name="services">Coleccion de servicios.</param>
    /// <returns>Coleccion para encadenamiento.</returns>
    public static IServiceCollection AddImpiTrackApplication(this IServiceCollection services)
    {
        services.AddScoped<IMeAccountService, MeAccountService>();
        services.AddScoped<IAdminUsersService, AdminUsersService>();
        services.AddScoped<ITelemetryQueryService, TelemetryQueryService>();

        // Comandos de dispositivo: validador stateless (singleton), notificador no-op por defecto
        // (la implementacion SignalR se registra en ImpiTrack.Api.Program.cs reemplazando este
        // registro con AddSingleton<IDeviceCommandNotifier, SignalRDeviceCommandNotifier>),
        // servicio orquestador como scoped.
        services.AddSingleton<IDeviceCommandValidator, DeviceCommandValidator>();
        services.TryAddSingleton<IDeviceCommandNotifier, NoOpDeviceCommandNotifier>();
        services.AddScoped<IDeviceCommandService, DeviceCommandService>();
        return services;
    }
}
