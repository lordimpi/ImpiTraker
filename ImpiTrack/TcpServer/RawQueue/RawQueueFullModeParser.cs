namespace TcpServer.RawQueue;

/// <summary>
/// Parseador de modos de cola raw desde configuracion textual.
/// </summary>
public static class RawQueueFullModeParser
{
    /// <summary>
    /// Convierte un texto de configuracion a <see cref="RawQueueFullMode"/>.
    /// </summary>
    /// <param name="value">Texto de modo configurado.</param>
    /// <returns>Modo de cola soportado.</returns>
    /// <exception cref="InvalidOperationException">Cuando el valor no es valido.</exception>
    public static RawQueueFullMode Parse(string? value)
    {
        if (string.Equals(value, "Wait", StringComparison.OrdinalIgnoreCase))
        {
            return RawQueueFullMode.Wait;
        }

        if (string.Equals(value, "Drop", StringComparison.OrdinalIgnoreCase))
        {
            return RawQueueFullMode.Drop;
        }

        if (string.Equals(value, "Disconnect", StringComparison.OrdinalIgnoreCase))
        {
            return RawQueueFullMode.Disconnect;
        }

        throw new InvalidOperationException("tcp_raw_full_mode_invalid supported=Wait|Drop|Disconnect");
    }
}
