using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TcpServer;

namespace ImpiTrack.Tests;

/// <summary>
/// Helper para remover hosted services del TcpServer durante tests de integracion API.
/// Evita que el Worker intente bindear puertos TCP dentro del proceso de test.
/// </summary>
internal static class TestHostedServiceHelper
{
    private static readonly Type[] TcpHostedServiceTypes =
    [
        typeof(Worker),
        typeof(InboundProcessingService),
        typeof(RawPacketProcessingService)
    ];

    /// <summary>
    /// Remueve las registraciones de IHostedService correspondientes al TcpServer
    /// (Worker, InboundProcessingService, RawPacketProcessingService) del contenedor de DI.
    /// </summary>
    public static void RemoveTcpHostedServices(IServiceCollection services)
    {
        List<ServiceDescriptor> toRemove = services
            .Where(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType is not null &&
                TcpHostedServiceTypes.Contains(d.ImplementationType))
            .ToList();

        foreach (ServiceDescriptor descriptor in toRemove)
        {
            services.Remove(descriptor);
        }
    }
}
