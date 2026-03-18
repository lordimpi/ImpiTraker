using ImpiTrack.Auth.Infrastructure.Auth.Contracts;
using ImpiTrack.Auth.Infrastructure.Auth.Models;
using ImpiTrack.Auth.Infrastructure.Configuration;
using ImpiTrack.Auth.Infrastructure.Email.Contracts;
using ImpiTrack.Auth.Infrastructure.Email.Models;
using ImpiTrack.Auth.Infrastructure.Identity;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.Shared.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace ImpiTrack.Auth.Infrastructure.Auth.Services;

/// <summary>
/// Servicio de emision y rotacion de tokens JWT y refresh tokens.
/// </summary>
public sealed class AuthTokenService : IAuthTokenService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IdentityAppDbContext _dbContext;
    private readonly IGenericOptionsService<JwtAuthOptions> _jwtOptionsService;
    private readonly IGenericOptionsService<IdentityBootstrapOptions> _bootstrapOptionsService;
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly IEmailDispatchQueue _emailDispatchQueue;
    private readonly IEmailTemplateRenderer _emailTemplateRenderer;
    private readonly ILogger<AuthTokenService> _logger;

    /// <summary>
    /// Crea el servicio de autenticacion basado en Identity y JWT.
    /// </summary>
    /// <param name="userManager">Administrador de usuarios.</param>
    /// <param name="roleManager">Administrador de roles.</param>
    /// <param name="signInManager">Administrador de sign-in.</param>
    /// <param name="dbContext">Contexto de persistencia de Identity.</param>
    /// <param name="jwtOptionsService">Opciones JWT tipadas.</param>
    /// <param name="bootstrapOptionsService">Opciones de bootstrap para nombres de rol.</param>
    /// <param name="userAccountRepository">Repositorio de cuentas y planes de usuario.</param>
    /// <param name="emailDispatchQueue">Cola asincrona para envio de correos.</param>
    /// <param name="emailTemplateRenderer">Constructor de plantillas de correo.</param>
    /// <param name="logger">Logger estructurado.</param>
    public AuthTokenService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        IdentityAppDbContext dbContext,
        IGenericOptionsService<JwtAuthOptions> jwtOptionsService,
        IGenericOptionsService<IdentityBootstrapOptions> bootstrapOptionsService,
        IUserAccountRepository userAccountRepository,
        IEmailDispatchQueue emailDispatchQueue,
        IEmailTemplateRenderer emailTemplateRenderer,
        ILogger<AuthTokenService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _jwtOptionsService = jwtOptionsService;
        _bootstrapOptionsService = bootstrapOptionsService;
        _userAccountRepository = userAccountRepository;
        _emailDispatchQueue = emailDispatchQueue;
        _emailTemplateRenderer = emailTemplateRenderer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegisterResult> RegisterAsync(
        string userName,
        string email,
        string password,
        string? fullName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedUserName = userName.Trim();
        string normalizedEmail = email.Trim();

        ApplicationUser? existingByUserName = await _userManager.FindByNameAsync(normalizedUserName);
        if (existingByUserName is not null)
        {
            return new RegisterResult(
                RegisterStatus.UserNameAlreadyExists,
                null,
                ["El nombre de usuario ya existe."]);
        }

        ApplicationUser? existingByEmail = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existingByEmail is not null)
        {
            return new RegisterResult(
                RegisterStatus.EmailAlreadyExists,
                null,
                ["El correo ya esta registrado."]);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = normalizedUserName,
            Email = normalizedEmail,
            EmailConfirmed = false
        };

        IdentityResult createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return new RegisterResult(
                RegisterStatus.Failed,
                null,
                createResult.Errors.Select(x => x.Description).ToArray());
        }

        IdentityBootstrapOptions bootstrapOptions = _bootstrapOptionsService.GetOptions();
        string userRole = string.IsNullOrWhiteSpace(bootstrapOptions.UserRole)
            ? "User"
            : bootstrapOptions.UserRole.Trim();

        await EnsureRoleExistsAsync(userRole);

        IdentityResult addRoleResult = await _userManager.AddToRoleAsync(user, userRole);
        if (!addRoleResult.Succeeded)
        {
            return new RegisterResult(
                RegisterStatus.Failed,
                null,
                addRoleResult.Errors.Select(x => x.Description).ToArray());
        }

        await _userAccountRepository.EnsureUserProvisioningAsync(
            user.Id,
            user.Email ?? normalizedEmail,
            string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim(),
            DateTimeOffset.UtcNow,
            cancellationToken);

        string emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        var response = new RegisterResponse(
            user.Id,
            user.UserName ?? normalizedUserName,
            user.Email ?? normalizedEmail,
            RequiresEmailVerification: true,
            EmailVerificationToken: emailToken);

        await EnqueueVerificationEmailAsync(user, normalizedUserName, normalizedEmail, emailToken, CancellationToken.None);

        return new RegisterResult(RegisterStatus.Created, response, []);
    }

    /// <inheritdoc />
    public async Task<VerifyEmailResult> VerifyEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new VerifyEmailResult(
                VerifyEmailStatus.UserNotFound,
                ["No existe el usuario solicitado."]);
        }

        if (user.EmailConfirmed)
        {
            return new VerifyEmailResult(VerifyEmailStatus.AlreadyVerified, []);
        }

        IdentityResult confirmResult = await _userManager.ConfirmEmailAsync(user, token);
        if (!confirmResult.Succeeded)
        {
            return new VerifyEmailResult(
                VerifyEmailStatus.InvalidToken,
                confirmResult.Errors.Select(x => x.Description).ToArray());
        }

        return new VerifyEmailResult(VerifyEmailStatus.Verified, []);
    }

    /// <inheritdoc />
    public async Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedEmail = email.Trim();
        ApplicationUser? user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            _logger.LogInformation("password_reset_request_ignored reason=user_not_found email={email}", normalizedEmail);
            return;
        }

        string passwordResetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        await EnqueuePasswordResetEmailAsync(user, normalizedEmail, passwordResetToken, CancellationToken.None);

        _logger.LogInformation("password_reset_requested userId={userId} email={email}", user.Id, normalizedEmail);
    }

    /// <inheritdoc />
    public async Task<AuthTokenPair?> LoginAsync(
        string userNameOrEmail,
        string password,
        string remoteIp,
        CancellationToken cancellationToken)
    {
        ApplicationUser? user = await _userManager.FindByNameAsync(userNameOrEmail);
        user ??= await _userManager.FindByEmailAsync(userNameOrEmail);
        if (user is null)
        {
            return null;
        }

        if (!user.EmailConfirmed)
        {
            return null;
        }

        SignInResult check = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            return null;
        }

        return await CreateTokenPairAsync(user, remoteIp, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ResetPasswordResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedEmail = email.Trim();
        ApplicationUser? user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            _logger.LogInformation("password_reset_rejected reason=user_not_found email={email}", normalizedEmail);
            return new ResetPasswordResult(ResetPasswordStatus.InvalidRequest, []);
        }

        IdentityResult resetResult = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!resetResult.Succeeded)
        {
            string[] errors = resetResult.Errors.Select(x => x.Description).ToArray();
            bool invalidToken = resetResult.Errors.Any(x =>
                string.Equals(x.Code, "InvalidToken", StringComparison.OrdinalIgnoreCase));

            _logger.LogWarning(
                "password_reset_failed userId={userId} email={email} invalidToken={invalidToken}",
                user.Id,
                normalizedEmail,
                invalidToken);

            return new ResetPasswordResult(
                invalidToken ? ResetPasswordStatus.InvalidRequest : ResetPasswordStatus.Failed,
                errors);
        }

        await _userManager.UpdateSecurityStampAsync(user);
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);
        await RevokeActiveRefreshTokensAsync(user.Id, cancellationToken);

        _logger.LogInformation("password_reset_completed userId={userId} email={email}", user.Id, normalizedEmail);
        return new ResetPasswordResult(ResetPasswordStatus.Succeeded, []);
    }

    /// <inheritdoc />
    public async Task<AuthTokenPair?> RefreshAsync(string refreshToken, string remoteIp, CancellationToken cancellationToken)
    {
        string tokenHash = ComputeSha256(refreshToken);

        RefreshToken? stored = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (stored is null || !stored.IsActive)
        {
            return null;
        }

        ApplicationUser? user = await _userManager.Users
            .SingleOrDefaultAsync(x => x.Id == stored.UserId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        AuthTokenPair pair = await CreateTokenPairAsync(user, remoteIp, cancellationToken);

        stored.RevokedAtUtc = DateTimeOffset.UtcNow;
        stored.RevokedByIp = remoteIp;
        stored.ReplacedByTokenHash = ComputeSha256(pair.RefreshToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return pair;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(string refreshToken, string remoteIp, CancellationToken cancellationToken)
    {
        string tokenHash = ComputeSha256(refreshToken);

        RefreshToken? stored = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (stored is null || !stored.IsActive)
        {
            return false;
        }

        stored.RevokedAtUtc = DateTimeOffset.UtcNow;
        stored.RevokedByIp = remoteIp;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<AuthTokenPair> CreateTokenPairAsync(
        ApplicationUser user,
        string remoteIp,
        CancellationToken cancellationToken)
    {
        JwtAuthOptions options = _jwtOptionsService.GetOptions();
        IList<string> roles = await _userManager.GetRolesAsync(user);

        DateTimeOffset accessExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(options.AccessTokenMinutes);
        DateTimeOffset refreshExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(options.RefreshTokenDays);

        string accessToken = CreateAccessToken(user, roles, options, accessExpiresAtUtc);
        string refreshToken = CreateRefreshToken();
        string refreshTokenHash = ComputeSha256(refreshToken);

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByIp = remoteIp,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokenPair(
            accessToken,
            accessExpiresAtUtc,
            refreshToken,
            refreshExpiresAtUtc);
    }

    private static string CreateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles,
        JwtAuthOptions options,
        DateTimeOffset expiresAtUtc)
    {
        byte[] signingKey = Encoding.UTF8.GetBytes(options.SigningKey);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        ];

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        foreach (string role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateRefreshToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private static string ComputeSha256(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private async Task EnsureRoleExistsAsync(string roleName)
    {
        if (await _roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        IdentityResult createResult = await _roleManager.CreateAsync(new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant()
        });

        if (!createResult.Succeeded &&
            createResult.Errors.All(x => !string.Equals(x.Code, "DuplicateRoleName", StringComparison.OrdinalIgnoreCase)))
        {
            string errors = string.Join(";", createResult.Errors.Select(x => x.Description));
            throw new InvalidOperationException($"identity_role_create_failed role={roleName} errors={errors}");
        }
    }

    private async Task RevokeActiveRefreshTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        List<RefreshToken> tokens = await _dbContext.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (RefreshToken token in tokens)
        {
            token.RevokedAtUtc = now;
            token.RevokedByIp = "password_reset";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnqueueVerificationEmailAsync(
        ApplicationUser user,
        string normalizedUserName,
        string normalizedEmail,
        string emailToken,
        CancellationToken cancellationToken)
    {
        string email = user.Email ?? normalizedEmail;
        string userName = user.UserName ?? normalizedUserName;
        EmailMessage message = _emailTemplateRenderer.BuildVerifyEmailMessage(user.Id, email, userName, emailToken);

        bool queued;
        try
        {
            queued = await _emailDispatchQueue.EnqueueAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "email_enqueue_exception userId={userId} email={email}",
                user.Id,
                email);

            return;
        }

        if (!queued)
        {
            _logger.LogWarning(
                "email_enqueue_timeout userId={userId} email={email}",
                user.Id,
                email);

            return;
        }

        _logger.LogInformation(
            "email_queued template={template} userId={userId} email={email}",
            message.TemplateName,
            user.Id,
            email);
    }

    private async Task EnqueuePasswordResetEmailAsync(
        ApplicationUser user,
        string normalizedEmail,
        string passwordResetToken,
        CancellationToken cancellationToken)
    {
        string email = user.Email ?? normalizedEmail;
        string userName = user.UserName ?? email;
        EmailMessage message = _emailTemplateRenderer.BuildResetPasswordMessage(user.Id, email, userName, passwordResetToken);

        bool queued;
        try
        {
            queued = await _emailDispatchQueue.EnqueueAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "password_reset_email_enqueue_exception userId={userId} email={email}",
                user.Id,
                email);

            return;
        }

        if (!queued)
        {
            _logger.LogWarning(
                "password_reset_email_enqueue_timeout userId={userId} email={email}",
                user.Id,
                email);

            return;
        }

        _logger.LogInformation(
            "password_reset_email_queued template={template} userId={userId} email={email}",
            message.TemplateName,
            user.Id,
            email);
    }
}

