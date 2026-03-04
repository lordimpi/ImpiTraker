namespace ImpiTrack.DataAccess.IOptionPattern;

/// <summary>
/// Contrato genérico para acceder a opciones estáticas, por snapshot y monitoreadas.
/// </summary>
/// <typeparam name="TOptions">Tipo de opciones configuradas.</typeparam>
public interface IGenericOptionsService<out TOptions>
    where TOptions : class, new()
{
    /// <summary>
    /// Obtiene las opciones de configuración estáticas.
    /// Estas opciones se cargan una vez y permanecen inmutables durante el ciclo de vida de la aplicación.
    /// </summary>
    /// <returns>Las opciones de configuración del tipo <typeparamref name="TOptions"/>.</returns>
    TOptions GetOptions();

    /// <summary>
    /// Obtiene una instantánea de las opciones de configuración.
    /// Útil para obtener configuraciones actualizadas dentro del contexto de una única solicitud HTTP.
    /// </summary>
    /// <returns>Las opciones de configuración del tipo <typeparamref name="TOptions"/>.</returns>
    TOptions GetSnapshotOptions();

    /// <summary>
    /// Obtiene opciones de configuración monitoreadas dinámicamente.
    /// Permite actualizaciones en tiempo real cuando cambia la fuente de configuración.
    /// </summary>
    /// <returns>Las opciones de configuración del tipo <typeparamref name="TOptions"/>.</returns>
    TOptions GetMonitorOptions();
}
