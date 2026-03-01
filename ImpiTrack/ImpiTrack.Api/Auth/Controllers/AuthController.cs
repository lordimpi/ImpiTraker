using ImpiTrack.Api.Auth.Contracts;
using ImpiTrack.Api.Auth.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Auth.Controllers;

/// <summary>
/// Endpoints de autenticación para login, refresh y revocación de tokens.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthTokenService _authTokenService;

    /// <summary>
    /// Crea un controlador de autenticación basado en tokens JWT.
    /// </summary>
    /// <param name="authTokenService">Servicio de tokens.</param>
    public AuthController(IAuthTokenService authTokenService)
    {
        _authTokenService = authTokenService;
    }

    /// <summary>
    /// Autentica credenciales y devuelve access token con refresh token.
    /// </summary>
    /// <param name="request">Credenciales de autenticación.</param>
    /// <param name="cancellationToken">Token de cancelación de la solicitud.</param>
    /// <returns>Par de tokens cuando la autenticación es exitosa.</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthTokenPair), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokenPair>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        string remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        AuthTokenPair? pair = await _authTokenService.LoginAsync(
            request.UserNameOrEmail,
            request.Password,
            remoteIp,
            cancellationToken);

        if (pair is null)
        {
            return Unauthorized();
        }

        return Ok(pair);
    }

    /// <summary>
    /// Renueva sesión con refresh token activo.
    /// </summary>
    /// <param name="request">Token de refresco vigente.</param>
    /// <param name="cancellationToken">Token de cancelación de la solicitud.</param>
    /// <returns>Nuevo par de tokens o 401 cuando el token no es válido.</returns>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthTokenPair), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokenPair>> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        string remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        AuthTokenPair? pair = await _authTokenService.RefreshAsync(
            request.RefreshToken,
            remoteIp,
            cancellationToken);

        if (pair is null)
        {
            return Unauthorized();
        }

        return Ok(pair);
    }

    /// <summary>
    /// Revoca un refresh token para cerrar sesión.
    /// </summary>
    /// <param name="request">Refresh token que se desea revocar.</param>
    /// <param name="cancellationToken">Token de cancelación de la solicitud.</param>
    /// <returns>200 cuando la revocación fue exitosa.</returns>
    [HttpPost("revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Revoke(
        [FromBody] RevokeRequest request,
        CancellationToken cancellationToken)
    {
        string remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        bool revoked = await _authTokenService.RevokeAsync(request.RefreshToken, remoteIp, cancellationToken);
        if (!revoked)
        {
            return BadRequest();
        }

        return Ok();
    }
}
