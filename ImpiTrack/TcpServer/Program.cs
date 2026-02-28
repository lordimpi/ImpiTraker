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
        builder.Services.AddTcpServerServices(builder.Configuration);

        IHost host = builder.Build();
        host.Run();
    }
}
