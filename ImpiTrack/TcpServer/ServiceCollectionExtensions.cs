using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Cantrack;
using ImpiTrack.Protocols.Coban;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.Correlation;
using ImpiTrack.Tcp.Core.Protocols;
using ImpiTrack.Tcp.Core.Queue;
using ImpiTrack.Tcp.Core.Sessions;
using Microsoft.Extensions.Options;

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
        services.Configure<TcpServerOptions>(configuration.GetSection(TcpServerOptions.SectionName));

        services.AddSingleton<ISessionManager, InMemorySessionManager>();
        services.AddSingleton<IPacketIdGenerator, GuidPacketIdGenerator>();

        services.AddSingleton<IProtocolResolver>(sp =>
        {
            TcpServerOptions options = sp.GetRequiredService<IOptions<TcpServerOptions>>().Value;
            return new PortFirstProtocolResolver(ProtocolMapBuilder.Build(options.Servers));
        });

        services.AddSingleton<IInboundQueue>(sp =>
        {
            TcpServerOptions options = sp.GetRequiredService<IOptions<TcpServerOptions>>().Value;
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
