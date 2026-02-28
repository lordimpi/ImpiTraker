namespace ImpiTrack.Tcp.Core.Configuration;

/// <summary>
/// Configuracion raiz para listeners de ingesta TCP, limites de socket y pipeline de cola.
/// </summary>
public sealed class TcpServerOptions
{
    /// <summary>
    /// Nombre de la seccion de configuracion en appsettings.
    /// </summary>
    public const string SectionName = "TcpServerConfig";

    /// <summary>
    /// Endpoints TCP configurados para escuchar.
    /// </summary>
    public List<TcpServerEndpointOptions> Servers { get; set; } = [];

    /// <summary>
    /// Limites a nivel socket y valores de timeout.
    /// </summary>
    public TcpSocketOptions Socket { get; set; } = new();

    /// <summary>
    /// Configuracion de cola entrante y workers consumidores.
    /// </summary>
    public TcpPipelineOptions Pipeline { get; set; } = new();

    /// <summary>
    /// Configuracion de seguridad y mitigacion de abuso por IP.
    /// </summary>
    public TcpSecurityOptions Security { get; set; } = new();
}
