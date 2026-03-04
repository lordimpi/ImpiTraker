using ImpiTrack.Tcp.Core.Configuration;

namespace TcpServer.Configuration;

/// <summary>
/// Resuelve la cantidad efectiva de workers de consumo a partir de opciones actuales y claves deprecadas.
/// </summary>
internal static class ConsumerWorkerResolver
{
    /// <summary>
    /// Calcula el resultado efectivo para workers de consumo.
    /// </summary>
    /// <param name="pipeline">Opciones de pipeline configuradas.</param>
    /// <param name="hasConsumerWorkersKey">Indica si la clave <c>ConsumerWorkers</c> viene explícita en configuración.</param>
    /// <param name="hasParserWorkersKey">Indica si la clave deprecada <c>ParserWorkers</c> viene explícita en configuración.</param>
    /// <param name="hasDbWorkersKey">Indica si la clave deprecada <c>DbWorkers</c> viene explícita en configuración.</param>
    /// <returns>Resultado con cantidad efectiva y metadatos de deprecación.</returns>
    internal static ConsumerWorkerResolution Resolve(
        TcpPipelineOptions pipeline,
        bool hasConsumerWorkersKey,
        bool hasParserWorkersKey,
        bool hasDbWorkersKey)
    {
        int normalizedConsumer = Math.Max(1, pipeline.ConsumerWorkers);
        bool hasDeprecatedKeys = hasParserWorkersKey || hasDbWorkersKey;

        if (hasConsumerWorkersKey || !hasDeprecatedKeys)
        {
            return new ConsumerWorkerResolution(
                normalizedConsumer,
                hasDeprecatedKeys,
                UsedDeprecatedFallback: false,
                ResolvedFrom: nameof(TcpPipelineOptions.ConsumerWorkers));
        }

        int parserWorkers = hasParserWorkersKey ? Math.Max(1, pipeline.ParserWorkers) : 0;
        int dbWorkers = hasDbWorkersKey ? Math.Max(1, pipeline.DbWorkers) : 0;
        int deprecatedValue = Math.Max(parserWorkers, dbWorkers);
        int effective = deprecatedValue > 0 ? deprecatedValue : normalizedConsumer;

        string resolvedFrom = parserWorkers > 0 && dbWorkers > 0
            ? $"{nameof(TcpPipelineOptions.ParserWorkers)}|{nameof(TcpPipelineOptions.DbWorkers)}"
            : parserWorkers > 0
                ? nameof(TcpPipelineOptions.ParserWorkers)
                : dbWorkers > 0
                    ? nameof(TcpPipelineOptions.DbWorkers)
                    : nameof(TcpPipelineOptions.ConsumerWorkers);

        return new ConsumerWorkerResolution(
            effective,
            HasDeprecatedKeys: true,
            UsedDeprecatedFallback: true,
            ResolvedFrom: resolvedFrom);
    }
}

/// <summary>
/// Resultado de resolución para workers de consumo.
/// </summary>
/// <param name="WorkerCount">Cantidad efectiva de workers a utilizar.</param>
/// <param name="HasDeprecatedKeys">Indica si se detectaron claves deprecadas en configuración.</param>
/// <param name="UsedDeprecatedFallback">Indica si se usó fallback desde claves deprecadas.</param>
/// <param name="ResolvedFrom">Nombre de clave o fuente efectiva utilizada.</param>
internal readonly record struct ConsumerWorkerResolution(
    int WorkerCount,
    bool HasDeprecatedKeys,
    bool UsedDeprecatedFallback,
    string ResolvedFrom);
