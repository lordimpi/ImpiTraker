namespace ImpiTrack.Tcp.Core.Configuration;

/// <summary>
/// Configuracion del bus de eventos interno para publicacion de eventos canonicos.
/// </summary>
public sealed class EventBusOptions
{
    /// <summary>
    /// Nombre de la seccion de configuracion.
    /// </summary>
    public const string SectionName = "EventBus";

    /// <summary>
    /// Provider del bus de eventos. Valores soportados: InMemory, Emqx.
    /// </summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Host del broker EMQX para conexiones MQTT.
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Puerto del broker EMQX para conexiones MQTT.
    /// </summary>
    public int Port { get; set; } = 1883;

    /// <summary>
    /// Identificador de cliente MQTT usado por el worker.
    /// </summary>
    public string ClientId { get; set; } = "imptrack-worker";

    /// <summary>
    /// Usuario MQTT opcional para autenticacion contra EMQX.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Contrasena MQTT opcional para autenticacion contra EMQX.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Indica si la conexion MQTT debe usar TLS.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// QoS MQTT para topics de telemetria.
    /// </summary>
    public int TelemetryQoS { get; set; } = 1;

    /// <summary>
    /// QoS MQTT para topics de estado.
    /// </summary>
    public int StatusQoS { get; set; } = 1;

    /// <summary>
    /// QoS MQTT para topics DLQ.
    /// </summary>
    public int DlqQoS { get; set; } = 1;

    /// <summary>
    /// Numero maximo de reintentos de publicacion antes de marcar fallo.
    /// </summary>
    public int MaxPublishRetries { get; set; } = 3;

    /// <summary>
    /// Backoff base en milisegundos para reintentos de publicacion.
    /// </summary>
    public int RetryBackoffMs { get; set; } = 500;

    /// <summary>
    /// Habilita publicacion de eventos en DLQ cuando se agotan reintentos.
    /// </summary>
    public bool EnableDlq { get; set; } = true;
}
