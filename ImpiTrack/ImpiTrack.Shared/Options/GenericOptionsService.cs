using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiTrack.Shared.Options;

/// <summary>
/// Implementación base para recuperar opciones estáticas, snapshot y monitoreadas.
/// </summary>
/// <typeparam name="TOptions">Tipo de opciones configuradas.</typeparam>
public sealed class GenericOptionsService<TOptions> : IGenericOptionsService<TOptions>
    where TOptions : class, new()
{
    private readonly IOptions<TOptions> _options;
    private readonly IOptionsMonitor<TOptions> _optionsMonitor;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Crea un servicio de opciones con soporte para modos estático, snapshot y monitor.
    /// </summary>
    /// <param name="options">Acceso estático a opciones.</param>
    /// <param name="optionsMonitor">Monitor de opciones para cambios en caliente.</param>
    /// <param name="scopeFactory">Fábrica de scopes para resolver snapshots.</param>
    public GenericOptionsService(
        IOptions<TOptions> options,
        IOptionsMonitor<TOptions> optionsMonitor,
        IServiceScopeFactory scopeFactory)
    {
        _options = options;
        _optionsMonitor = optionsMonitor;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public TOptions GetOptions() => _options.Value;

    /// <inheritdoc />
    public TOptions GetSnapshotOptions()
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<TOptions>>().Value;
    }

    /// <inheritdoc />
    public TOptions GetMonitorOptions() => _optionsMonitor.CurrentValue;
}
