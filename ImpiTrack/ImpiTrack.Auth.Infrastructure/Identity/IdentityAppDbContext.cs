using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ImpiTrack.Auth.Infrastructure.Identity;

/// <summary>
/// Contexto EF Core de Identity para usuarios, roles y refresh tokens.
/// </summary>
public sealed class IdentityAppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    /// <summary>
    /// Crea un contexto de Identity con opciones configuradas por DI.
    /// </summary>
    /// <param name="options">Opciones del contexto.</param>
    public IdentityAppDbContext(DbContextOptions<IdentityAppDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Conjunto de tokens de refresco emitidos.
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("IdentityUsers");
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("IdentityRoles");
        });

        builder.Entity<IdentityUserRole<Guid>>(entity =>
        {
            entity.ToTable("IdentityUserRoles");
        });

        builder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("IdentityUserClaims");
        });

        builder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("IdentityUserLogins");
        });

        builder.Entity<IdentityRoleClaim<Guid>>(entity =>
        {
            entity.ToTable("IdentityRoleClaims");
        });

        builder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("IdentityUserTokens");
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("IdentityRefreshTokens");
            entity.HasKey(x => x.RefreshTokenId);

            entity.Property(x => x.TokenHash)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.CreatedByIp)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(x => x.RevokedByIp)
                .HasMaxLength(64);

            entity.Property(x => x.ReplacedByTokenHash)
                .HasMaxLength(200);

            entity.HasIndex(x => x.TokenHash)
                .IsUnique();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
