namespace TcpServer.RawQueue;

/// <summary>
/// Cola acotada para persistencia diferida de paquetes raw.
/// </summary>
public interface IRawPacketQueue
{
    /// <summary>
    /// Obtiene el numero actual de elementos pendientes en cola.
    /// </summary>
    long Backlog { get; }

    /// <summary>
    /// Modo configurado para reaccionar ante cola llena.
    /// </summary>
    RawQueueFullMode FullMode { get; }

    /// <summary>
    /// Encola un paquete raw de forma asincrona.
    /// </summary>
    /// <param name="envelope">Elemento a encolar.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si se encolo; <c>false</c> si se rechazo por capacidad.</returns>
    ValueTask<bool> EnqueueAsync(RawPacketEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>
    /// Desencola un paquete raw disponible.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Elemento encolado.</returns>
    ValueTask<RawPacketEnvelope> DequeueAsync(CancellationToken cancellationToken);
}
