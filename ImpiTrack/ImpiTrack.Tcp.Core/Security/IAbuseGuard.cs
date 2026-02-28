namespace ImpiTrack.Tcp.Core.Security;

/// <summary>
/// Define reglas de limitacion y bloqueo temporal de trafico por IP.
/// </summary>
public interface IAbuseGuard
{
    /// <summary>
    /// Evalua si una IP esta bloqueada temporalmente.
    /// </summary>
    /// <param name="remoteIp">IP remota evaluada.</param>
    /// <param name="nowUtc">Instante UTC de evaluacion.</param>
    /// <param name="blockedUntilUtc">Instante UTC hasta donde aplica el bloqueo.</param>
    /// <returns><c>true</c> si la IP esta bloqueada; de lo contrario <c>false</c>.</returns>
    bool IsBlocked(string remoteIp, DateTimeOffset nowUtc, out DateTimeOffset? blockedUntilUtc);

    /// <summary>
    /// Registra un frame observado para una IP y actualiza contadores de abuso.
    /// </summary>
    /// <param name="remoteIp">IP remota observada.</param>
    /// <param name="isInvalid">Indica si el frame fue invalido.</param>
    /// <param name="nowUtc">Instante UTC del evento.</param>
    void RegisterFrame(string remoteIp, bool isInvalid, DateTimeOffset nowUtc);
}
