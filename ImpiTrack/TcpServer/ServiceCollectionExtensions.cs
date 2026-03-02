using ImpiTrack.DataAccess.Extensions;
using ImpiTrack.DataAccess.IOptionPattern;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Cantrack;
using ImpiTrack.Protocols.Coban;
using ImpiTrack.Observability;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.Correlation;
using ImpiTrack.Tcp.Core.Protocols;
using ImpiTrack.Tcp.Core.Queue;
using ImpiTrack.Tcp.Core.Security;
using ImpiTrack.Tcp.Core.Sessions;

namespace TcpServer;

/// <summary>
/// Bootstrap de inyeccion de dependencias para el runtime de ingesta TCP.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra servicios TCP de Fase 1, modulos de protocolo y workers hosteados.
    /// </summary>
    /// <param name="services">Coleccion de servicios a configurar.</param>
    /// <param name="configuration">Raiz de configuracion de la aplicacion.</param>
    /// <returns>La misma coleccion para encadenamiento fluido.</returns>
    public static IServiceCollection AddTcpServerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddImpiTrackOptionsCore();
        services
            .BindOptions<TcpServerOptions>(
                configuration,
                TcpServerOptions.SectionName,
                validateDataAnnotations: false,
                validateOnStart: true)
            .Validate(
                static options => options.Servers.Count > 0,
                "TcpServerConfig:Servers debe contener al menos un endpoint.");

        services.AddImpiTrackDataAccess(configuration);

        services.AddSingleton<ISessionManager, InMemorySessionManager>();
        services.AddSingleton<IPacketIdGenerator, GuidPacketIdGenerator>();
        services.AddSingleton<ITcpMetrics, TcpMetrics>();
        services.AddSingleton<IAbuseGuard>(sp =>
        {
            TcpServerOptions options = sp.GetRequiredService<IGenericOptionsService<TcpServerOptions>>().GetOptions();
            return new InMemoryAbuseGuard(options.Security);
        });

        services.AddSingleton<IProtocolResolver>(sp =>
        {
            TcpServerOptions options = sp.GetRequiredService<IGenericOptionsService<TcpServerOptions>>().GetOptions();
            return new PortFirstProtocolResolver(ProtocolMapBuilder.Build(options.Servers));
        });

        services.AddSingleton<IInboundQueue>(sp =>
        {
            TcpServerOptions options = sp.GetRequiredService<IGenericOptionsService<TcpServerOptions>>().GetOptions();
            return new InMemoryInboundQueue(options.Pipeline.ChannelCapacity);
        });

        services.AddSingleton<IProtocolParser, CobanProtocolParser>();
        services.AddSingleton<IProtocolParser, CantrackProtocolParser>();
        services.AddSingleton<IAckStrategy, CobanAckStrategy>();
        services.AddSingleton<IAckStrategy, CantrackAckStrategy>();

        services.AddHostedService<Worker>();
        services.AddHostedService<InboundProcessingService>();
        return services;
    }
}

