using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ImpiTrack.DataAccess.IOptionPattern;

/// <summary>
/// Extensiones para registrar y vincular opciones tipadas con validacion de inicio.
/// </summary>
public static class OptionsPatternServiceCollectionExtensions
{
    /// <summary>
    /// Registra infraestructura base para opciones y el servicio generico de acceso.
    /// </summary>
    /// <param name="services">Coleccion de servicios.</param>
    /// <returns>La misma coleccion para encadenamiento.</returns>
    public static IServiceCollection AddImpiTrackOptionsCore(this IServiceCollection services)
    {
        services.AddOptions();
        services.TryAddSingleton(typeof(IGenericOptionsService<>), typeof(GenericOptionsService<>));
        return services;
    }

    /// <summary>
    /// Vincula una clase de opciones a una seccion de configuracion con validaciones opcionales.
    /// </summary>
    /// <typeparam name="TOptions">Tipo de opciones a enlazar.</typeparam>
    /// <param name="services">Coleccion de servicios.</param>
    /// <param name="configuration">Raiz de configuracion.</param>
    /// <param name="sectionName">Nombre de seccion que contiene las opciones.</param>
    /// <param name="validateDataAnnotations">Indica si se aplican validaciones DataAnnotations.</param>
    /// <param name="validateOnStart">Indica si se valida al arrancar el host.</param>
    /// <returns>Builder de opciones para reglas adicionales.</returns>
    public static OptionsBuilder<TOptions> BindOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        bool validateDataAnnotations = true,
        bool validateOnStart = true)
        where TOptions : class, new()
    {
        OptionsBuilder<TOptions> builder = services
            .AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName));

        if (validateDataAnnotations)
        {
            builder.ValidateDataAnnotations();
        }

        if (validateOnStart)
        {
            builder.ValidateOnStart();
        }

        return builder;
    }
}
