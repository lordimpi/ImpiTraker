using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ImpiTrack.Api.Auth.Contracts;
using ImpiTrack.Api.Auth.Models;
using ImpiTrack.Api.Configuration;
using ImpiTrack.Api.Identity;
using ImpiTrack.DataAccess.IOptionPattern;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ImpiTrack.Api.Auth.Services;

/// <summary>
/// Servicio de emisión y rotación de tokens JWT y refresh tokens.
/// </summary>
public sealed class AuthTokenService : IAuthTokenService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IdentityAppDbContext _dbContext;
    private readonly IGenericOptionsService<JwtAuthOptions> _jwtOptionsService;

    /// <summary>
    /// Crea el servicio de autenticación basado en Identity y JWT.
    /// </summary>
    /// <param name="userManager">Administrador de usuarios.</param>
    /// <param name="signInManager">Administrador de sign-in.</param>
    /// <param name="dbContext">Contexto de persistencia de Identity.</param>
    /// <param name="jwtOptionsService">Opciones JWT tipadas.</param>
    public AuthTokenService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IdentityAppDbContext dbContext,
        IGenericOptionsService<JwtAuthOptions> jwtOptionsService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _jwtOptionsService = jwtOptionsService;
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

        SignInResult check = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            return null;
        }

        return await CreateTokenPairAsync(user, remoteIp, cancellationToken);
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
}
