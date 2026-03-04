using ImpiTrack.Ops;
using ImpiTrack.Tcp.Core.Queue;

namespace ImpiTrack.DataAccess.Abstractions;

/// <summary>
/// Resultado de persistencia de un envelope downstream.
/// </summary>
public enum PersistEnvelopeStatus
{
    /// <summary>
    /// El envelope se persistio correctamente.
    /// </summary>
    Persisted = 1,

    /// <summary>
    /// El envelope fue identificado como duplicado y no genero nueva escritura.
    /// </summary>
    Deduplicated = 2,

    /// <summary>
    /// El envelope se omite porque no hay dispositivo vinculado para el IMEI.
    /// </summary>
    SkippedUnownedDevice = 3
}

/// <summary>
/// Valor de retorno de persistencia para telemetria/eventos normalizados.
/// </summary>
/// <param name="Status">Estado final de persistencia aplicado.</param>
public readonly record struct PersistEnvelopeResult(PersistEnvelopeStatus Status);

/// <summary>
/// Operaciones de escritura de ingesta para sesiones, paquetes y datos normalizados.
/// </summary>
public interface IIngestionRepository
{
    /// <summary>
    /// Inserta un paquete raw de evidencia operativa.
    /// </summary>
    /// <param name="record">Registro raw a persistir.</param>
    /// <param name="backlog">Backlog observado en el canal al momento del encolado.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task AddRawPacketAsync(RawPacketRecord record, long backlog, CancellationToken cancellationToken);

    /// <summary>
    /// Inserta o actualiza el estado de una sesion TCP.
    /// </summary>
    /// <param name="session">Snapshot de sesion a guardar.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task UpsertSessionAsync(SessionRecord session, CancellationToken cancellationToken);

    /// <summary>
    /// Persiste informacion normalizada derivada de un mensaje parseado.
    /// </summary>
    /// <param name="envelope">Envelope parseado consumido de la cola.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado de persistencia para telemetria/evento deduplicado.</returns>
    Task<PersistEnvelopeResult> PersistEnvelopeAsync(InboundEnvelope envelope, CancellationToken cancellationToken);
}
