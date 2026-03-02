using ImpiTrack.Auth.Infrastructure.Configuration;
using ImpiTrack.Auth.Infrastructure.Identity;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.IOptionPattern;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpiTrack.Auth.Infrastructure.Auth.Services;

/// <summary>
/// Servicio de arranque para asegurar roles de Identity y sembrar administrador inicial.
/// </summary>
public sealed class IdentityBootstrapHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGenericOptionsService<IdentityBootstrapOptions> _bootstrapOptionsService;
    private readonly ILogger<IdentityBootstrapHostedService> _logger;

    /// <summary>
    /// Crea el servicio de bootstrap de Identity.
    /// </summary>
    /// <param name="serviceProvider">Proveedor de servicios raiz.</param>
    /// <param name="bootstrapOptionsService">Opciones de bootstrap de Identity.</param>
    /// <param name="logger">Logger estructurado.</param>
    public IdentityBootstrapHostedService(
        IServiceProvider serviceProvider,
        IGenericOptionsService<IdentityBootstrapOptions> bootstrapOptionsService,
        ILogger<IdentityBootstrapHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _bootstrapOptionsService = bootstrapOptionsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();

        IdentityBootstrapOptions options = _bootstrapOptionsService.GetOptions();
        RoleManager<ApplicationRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        string adminRole = options.AdminRole.Trim();
        string userRole = string.IsNullOrWhiteSpace(options.UserRole) ? "User" : options.UserRole.Trim();

        await EnsureRoleExistsAsync(roleManager, adminRole);
        if (!string.Equals(userRole, adminRole, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureRoleExistsAsync(roleManager, userRole);
        }

        if (!options.SeedAdminOnStart)
        {
            _logger.LogInformation(
                "identity_roles_seed_completed adminRole={adminRole} userRole={userRole}",
                adminRole,
                userRole);

            return;
        }

        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IUserAccountRepository userAccountRepository = scope.ServiceProvider.GetRequiredService<IUserAccountRepository>();

        ApplicationUser? user = await userManager.FindByNameAsync(options.AdminUserName);
        user ??= await userManager.FindByEmailAsync(options.AdminEmail);

        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = options.AdminUserName,
                Email = options.AdminEmail,
                EmailConfirmed = true
            };

            IdentityResult createResult = await userManager.CreateAsync(user, options.AdminPassword);
            if (!createResult.Succeeded)
            {
                string errors = string.Join(";", createResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"identity_seed_user_failed errors={errors}");
            }
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            IdentityResult updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                string errors = string.Join(";", updateResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"identity_seed_user_confirm_failed errors={errors}");
            }
        }

        if (!await userManager.IsInRoleAsync(user, adminRole))
        {
            IdentityResult addRoleResult = await userManager.AddToRoleAsync(user, adminRole);
            if (!addRoleResult.Succeeded)
            {
                string errors = string.Join(";", addRoleResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"identity_seed_user_role_failed errors={errors}");
            }
        }

        await userAccountRepository.EnsureUserProvisioningAsync(
            user.Id,
            user.Email ?? options.AdminEmail,
            fullName: null,
            DateTimeOffset.UtcNow,
            cancellationToken);

        _logger.LogInformation(
            "identity_seed_completed adminUser={adminUser} adminRole={adminRole} userRole={userRole}",
            options.AdminUserName,
            adminRole,
            userRole);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static async Task EnsureRoleExistsAsync(RoleManager<ApplicationRole> roleManager, string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        IdentityResult roleResult = await roleManager.CreateAsync(new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant()
        });

        if (!roleResult.Succeeded)
        {
            string errors = string.Join(";", roleResult.Errors.Select(x => x.Description));
            throw new InvalidOperationException($"identity_seed_role_failed role={roleName} errors={errors}");
        }
    }
}
