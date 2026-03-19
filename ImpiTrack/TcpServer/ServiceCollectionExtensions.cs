using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Extensions;
using ImpiTrack.Shared.Options;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Protocols.Cantrack;
using ImpiTrack.Protocols.Coban;
using ImpiTrack.Observability;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.Correlation;
using ImpiTrack.Tcp.Core.EventBus;
using ImpiTrack.Tcp.Core.Protocols;
using ImpiTrack.Tcp.Core.Queue;
using ImpiTrack.Tcp.Core.Security;
using ImpiTrack.Tcp.Core.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TcpServer.EventBus;
using TcpServer.RawQueue;

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
        EnsureNoDeprecatedPipelineKeys(configuration);

        services.AddImpiTrackOptionsCore();
        services
            .BindOptions<TcpServerOptions>(
                configuration,
                TcpServerOptions.SectionName,
                validateDataAnnotations: false,
                validateOnStart: true)
            .Validate(
                static options => options.Servers.Count > 0,
                "TcpServerConfig:Servers debe contener al menos un endpoint.")
            .Validate(
                static options => options.Pipeline.ConsumerWorkers >= 1,
                "TcpServerConfig:Pipeline:ConsumerWorkers debe ser >= 1.");
        services
            .BindOptions<EventBusOptions>(
                configuration,
                EventBusOptions.SectionName,
                validateDataAnnotations: false,
                validateOnStart: true)
            .Validate(
                static options =>
                    string.Equals(options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(options.Provider, "Emqx", StringComparison.OrdinalIgnoreCase),
                "EventBus:Provider debe ser InMemory o Emqx.")
            .Validate(
                static options => options.Port is > 0 and <= 65535,
                "EventBus:Port debe estar entre 1 y 65535.")
            .Validate(
                static options =>
                    options.TelemetryQoS is >= 0 and <= 2 &&
                    options.StatusQoS is >= 0 and <= 2 &&
                    options.DlqQoS is >= 0 and <= 2,
                "EventBus:*QoS debe estar entre 0 y 2.")
            .Validate(
                static options => options.MaxPublishRetries >= 0 && options.RetryBackoffMs > 0,
                "EventBus:MaxPublishRetries debe ser >= 0 y RetryBackoffMs > 0.")
            .Validate(
                static options =>
                    !options.EnablePublishFailureSimulation ||
                    string.IsNullOrWhiteSpace(options.SimulateFailureEventType) ||
                    string.Equals(options.SimulateFailureEventType, "telemetry_v1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(options.SimulateFailureEventType, "status_v1", StringComparison.OrdinalIgnoreCase),
                "EventBus:SimulateFailureEventType debe ser telemetry_v1 o status_v1 cuando la simulacion esta habilitada.");

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
        services.AddSingleton<IRawPacketQueue>(sp =>
        {
            TcpServerOptions options = sp.GetRequiredService<IGenericOptionsService<TcpServerOptions>>().GetOptions();
            RawQueueFullMode fullMode = RawQueueFullModeParser.Parse(options.Pipeline.RawFullMode);
            return new InMemoryRawPacketQueue(options.Pipeline.RawChannelCapacity, fullMode);
        });

        services.AddSingleton<IEventBus>(sp =>
        {
            EventBusOptions eventBusOptions = sp.GetRequiredService<IGenericOptionsService<EventBusOptions>>().GetOptions();
            if (string.Equals(eventBusOptions.Provider, "Emqx", StringComparison.OrdinalIgnoreCase))
            {
                return ActivatorUtilities.CreateInstance<EmqxMqttEventBus>(sp);
            }

            return new InMemoryEventBus();
        });

        services.AddSingleton<IProtocolParser, CobanProtocolParser>();
        services.AddSingleton<IProtocolParser, CantrackProtocolParser>();
        services.AddSingleton<IAckStrategy, CobanAckStrategy>();
        services.AddSingleton<IAckStrategy, CantrackAckStrategy>();

        services.TryAddSingleton<ITelemetryNotifier, NullTelemetryNotifier>();

        services.AddHostedService<Worker>();
        services.AddHostedService<InboundProcessingService>();
        services.AddHostedService<RawPacketProcessingService>();
        return services;
    }

    /// <summary>
    /// Valida que no se usen claves de configuracion retiradas en el pipeline TCP.
    /// </summary>
    /// <param name="configuration">Configuracion raiz de la aplicacion.</param>
    /// <exception cref="OptionsValidationException">Se lanza cuando se detectan claves deprecadas.</exception>
    internal static void EnsureNoDeprecatedPipelineKeys(IConfiguration configuration)
    {
        IConfigurationSection pipelineSection = configuration.GetSection($"{TcpServerOptions.SectionName}:Pipeline");
        List<string> deprecatedKeys = [];

        if (!string.IsNullOrWhiteSpace(pipelineSection["ParserWorkers"]))
        {
            deprecatedKeys.Add("ParserWorkers");
        }

        if (!string.IsNullOrWhiteSpace(pipelineSection["DbWorkers"]))
        {
            deprecatedKeys.Add("DbWorkers");
        }

        if (deprecatedKeys.Count == 0)
        {
            return;
        }

        throw new OptionsValidationException(
            nameof(TcpServerOptions),
            typeof(TcpServerOptions),
            [
                $"TcpServerConfig:Pipeline contiene claves retiradas ({string.Join(", ", deprecatedKeys)}). Usa solo ConsumerWorkers."
            ]);
    }
}

