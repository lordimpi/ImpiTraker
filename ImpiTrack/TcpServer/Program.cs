using OpenTelemetry.Metrics;

namespace TcpServer;

/// <summary>
/// Punto de entrada de la aplicacion para el host worker TCP.
/// </summary>
public class Program
{
    /// <summary>
    /// Inicializa el host, registra servicios e inicia workers en segundo plano.
    /// </summary>
    /// <param name="args">Argumentos de linea de comandos.</param>
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureOpenTelemetry(builder);
        builder.Services.AddTcpServerServices(builder.Configuration);

        IHost host = builder.Build();
        host.Run();
    }

    private static void ConfigureOpenTelemetry(HostApplicationBuilder builder)
    {
        bool enabled = builder.Configuration.GetValue("OpenTelemetry:Enabled", false);
        if (!enabled)
        {
            return;
        }

        string? endpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter("ImpiTrack.Tcp")
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        if (!string.IsNullOrWhiteSpace(endpoint))
                        {
                            options.Endpoint = new Uri(endpoint);
                        }
                    });
            });
    }
}
