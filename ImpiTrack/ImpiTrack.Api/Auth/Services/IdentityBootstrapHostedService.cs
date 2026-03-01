using ImpiTrack.Api.Configuration;
using ImpiTrack.Api.Identity;
using ImpiTrack.DataAccess.IOptionPattern;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ImpiTrack.Api.Auth.Services;

/// <summary>
/// Servicio de arranque para asegurar esquema de Identity y sembrar administrador inicial.
/// </summary>
public sealed class IdentityBootstrapHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGenericOptionsService<IdentityBootstrapOptions> _bootstrapOptionsService;
    private readonly ILogger<IdentityBootstrapHostedService> _logger;

    /// <summary>
    /// Crea el servicio de bootstrap de Identity.
    /// </summary>
    /// <param name="serviceProvider">Proveedor de servicios raíz.</param>
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
        IdentityAppDbContext dbContext = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();

        // Nota: EnsureCreated permite operar sin exigir migraciones EF pre-generadas en esta fase.
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        IdentityBootstrapOptions options = _bootstrapOptionsService.GetOptions();
        if (!options.SeedAdminOnStart)
        {
            return;
        }

        RoleManager<ApplicationRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(options.AdminRole))
        {
            IdentityResult roleResult = await roleManager.CreateAsync(new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = options.AdminRole,
                NormalizedName = options.AdminRole.ToUpperInvariant()
            });

            if (!roleResult.Succeeded)
            {
                string errors = string.Join(";", roleResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"identity_seed_role_failed errors={errors}");
            }
        }

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

        if (!await userManager.IsInRoleAsync(user, options.AdminRole))
        {
            IdentityResult addRoleResult = await userManager.AddToRoleAsync(user, options.AdminRole);
            if (!addRoleResult.Succeeded)
            {
                string errors = string.Join(";", addRoleResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"identity_seed_user_role_failed errors={errors}");
            }
        }

        _logger.LogInformation(
            "identity_seed_completed adminUser={adminUser} adminRole={adminRole}",
            options.AdminUserName,
            options.AdminRole);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
