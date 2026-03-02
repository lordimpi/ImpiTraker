using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Configuration;

/// <summary>
/// Opciones de despacho asincrono para cola de correos.
/// </summary>
public sealed class EmailDispatchOptions
{
    /// <summary>
    /// Nombre de la seccion de configuracion.
    /// </summary>
    public const string SectionName = "EmailDispatch";

    /// <summary>
    /// Capacidad maxima de la cola en memoria.
    /// </summary>
    [Range(1, 100_000)]
    public int ChannelCapacity { get; set; } = 1_000;

    /// <summary>
    /// Tiempo maximo para esperar espacio en cola antes de descartar el mensaje.
    /// </summary>
    [Range(10, 60_000)]
    public int EnqueueTimeoutMs { get; set; } = 1_000;
}
