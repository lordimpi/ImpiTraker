using System.Net;
using ImpiTrack.Api.Http;
using ImpiTrack.Auth.Infrastructure.Auth.Contracts;
using ImpiTrack.Auth.Infrastructure.Auth.Models;
using ImpiTrack.Shared.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Auth.Controllers;

/// <summary>
/// Endpoints de autenticacion para registro, login, refresh y revocacion de tokens.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthTokenService _authTokenService;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Crea un controlador de autenticacion basado en tokens JWT.
    /// </summary>
    /// <param name="authTokenService">Servicio de autenticacion y tokens.</param>
    /// <param name="environment">Entorno de ejecucion del host.</param>
    public AuthController(IAuthTokenService authTokenService, IWebHostEnvironment environment)
    {
        _authTokenService = authTokenService;
        _environment = environment;
    }

    /// <summary>
    /// Registra un nuevo usuario y devuelve token de verificacion de correo.
    /// </summary>
    /// <param name="request">Datos de registro de la cuenta.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resultado del registro.</returns>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResult>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<RegisterResult>>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        RegisterResult result = await _authTokenService.RegisterAsync(
            request.UserName,
            request.Email,
            request.Password,
            request.FullName,
            cancellationToken);

        return result.Status switch
        {
            RegisterStatus.Created => StatusCode(
                StatusCodes.Status201Created,
                ApiResponseFactory.Success(HideVerificationTokenWhenRequired(result), HttpContext.TraceIdentifier)),
            RegisterStatus.UserNameAlreadyExists => this.FailEnvelope<RegisterResult>(
                StatusCodes.Status409Conflict,
                "username_already_exists",
                "No fue posible registrar la cuenta con los datos enviados."),
            RegisterStatus.EmailAlreadyExists => this.FailEnvelope<RegisterResult>(
                StatusCodes.Status409Conflict,
                "email_already_exists",
                "No fue posible registrar la cuenta con los datos enviados."),
            _ => this.FailEnvelope<RegisterResult>(
                StatusCodes.Status400BadRequest,
                "registration_failed",
                "No fue posible completar el registro.")
        };
    }

    /// <summary>
    /// Confirma correo por enlace GET usando userId y token en query string.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="token">Token de confirmacion emitido por Identity.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Pagina HTML con el resultado de la verificacion.</returns>
    [HttpGet("verify-email/confirm")]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail(
        [FromQuery] Guid userId,
        [FromQuery] string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return HtmlConfirmationPage(
                StatusCodes.Status400BadRequest,
                "Token invalido",
                "No fue posible verificar el correo con el token proporcionado.");
        }

        VerifyEmailResult result = await _authTokenService.VerifyEmailAsync(userId, token, cancellationToken);
        return result.Status switch
        {
            VerifyEmailStatus.Verified => HtmlConfirmationPage(
                StatusCodes.Status200OK,
                "Correo verificado",
                "Tu correo fue confirmado correctamente. Ya puedes iniciar sesion en ImpiTrack."),
            VerifyEmailStatus.AlreadyVerified => HtmlConfirmationPage(
                StatusCodes.Status200OK,
                "Correo ya verificado",
                "Este correo ya estaba confirmado. Puedes iniciar sesion en ImpiTrack."),
            VerifyEmailStatus.UserNotFound => HtmlConfirmationPage(
                StatusCodes.Status404NotFound,
                "Cuenta no encontrada",
                "No existe la cuenta solicitada."),
            _ => HtmlConfirmationPage(
                StatusCodes.Status400BadRequest,
                "Token invalido",
                "No fue posible verificar el correo con el token proporcionado.")
        };
    }

    /// <summary>
    /// Verifica el correo de un usuario usando el token emitido en registro.
    /// </summary>
    /// <param name="request">Identificador de usuario y token de confirmacion.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resultado de verificacion.</returns>
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(ApiResponse<VerifyEmailResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<VerifyEmailResult>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<VerifyEmailResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<VerifyEmailResult>>> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        VerifyEmailResult result = await _authTokenService.VerifyEmailAsync(
            request.UserId,
            request.Token,
            cancellationToken);

        return result.Status switch
        {
            VerifyEmailStatus.Verified => this.OkEnvelope(result),
            VerifyEmailStatus.AlreadyVerified => this.OkEnvelope(result),
            VerifyEmailStatus.UserNotFound => this.FailEnvelope<VerifyEmailResult>(
                StatusCodes.Status404NotFound,
                "user_not_found",
                "No existe la cuenta solicitada."),
            _ => this.FailEnvelope<VerifyEmailResult>(
                StatusCodes.Status400BadRequest,
                "invalid_email_verification_token",
                "No fue posible verificar el correo con el token proporcionado.")
        };
    }

    /// <summary>
    /// Autentica credenciales y devuelve access token con refresh token.
    /// </summary>
    /// <param name="request">Credenciales de autenticacion.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Par de tokens cuando la autenticacion es exitosa.</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthTokenPair>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthTokenPair>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthTokenPair>>> Login(
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
            return this.FailEnvelope<AuthTokenPair>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        return this.OkEnvelope(pair);
    }

    /// <summary>
    /// Renueva sesion con refresh token activo.
    /// </summary>
    /// <param name="request">Token de refresco vigente.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Nuevo par de tokens o 401 cuando el token no es valido.</returns>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthTokenPair>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthTokenPair>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthTokenPair>>> Refresh(
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
            return this.FailEnvelope<AuthTokenPair>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        return this.OkEnvelope(pair);
    }

    /// <summary>
    /// Revoca un refresh token para cerrar sesion.
    /// </summary>
    /// <param name="request">Refresh token que se desea revocar.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>200 cuando la revocacion fue exitosa.</returns>
    [HttpPost("revoke")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<object?>>> Revoke(
        [FromBody] RevokeRequest request,
        CancellationToken cancellationToken)
    {
        string remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        bool revoked = await _authTokenService.RevokeAsync(request.RefreshToken, remoteIp, cancellationToken);
        if (!revoked)
        {
            return this.FailEnvelope<object?>(
                StatusCodes.Status400BadRequest,
                "refresh_token_invalid",
                "No fue posible revocar el token solicitado.");
        }

        return this.OkEnvelope();
    }

    private RegisterResult HideVerificationTokenWhenRequired(RegisterResult result)
    {
        if (!_environment.IsProduction() || result.Registration is null)
        {
            return result;
        }

        RegisterResponse sanitized = result.Registration with
        {
            EmailVerificationToken = string.Empty
        };

        return result with
        {
            Registration = sanitized
        };
    }

    private ContentResult HtmlConfirmationPage(int statusCode, string title, string message)
    {
        string safeTitle = WebUtility.HtmlEncode(title);
        string safeMessage = WebUtility.HtmlEncode(message);

        string body = $"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1" />
              <title>{safeTitle}</title>
            </head>
            <body style="margin:0;padding:24px;background:#f4f7fb;font-family:Segoe UI,Arial,sans-serif;color:#102a43;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #d9e2ec;border-radius:12px;">
                <tr>
                  <td style="padding:24px;">
                    <h1 style="margin:0 0 12px;font-size:22px;line-height:1.3;">{safeTitle}</h1>
                    <p style="margin:0 0 16px;font-size:15px;line-height:1.6;">{safeMessage}</p>
                    <p style="margin:0;font-size:13px;color:#486581;">Puedes cerrar esta ventana y volver a la aplicacion.</p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "text/html; charset=utf-8",
            Content = body
        };
    }
}
