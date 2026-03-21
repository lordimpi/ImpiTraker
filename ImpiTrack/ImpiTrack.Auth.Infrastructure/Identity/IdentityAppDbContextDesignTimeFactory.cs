using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ImpiTrack.Auth.Infrastructure.Identity;

/// <summary>
/// Factoría de diseño para <see cref="IdentityAppDbContext"/>.
/// Permite ejecutar <c>dotnet ef migrations add</c> sin necesidad de levantar
/// el pipeline completo de la aplicación.
/// Variables de entorno:
///   EF_IDENTITY_PROVIDER   — "SqlServer" (default) o "Postgres"
///   EF_IDENTITY_CONNECTION — cadena de conexión (puede ser dummy para scaffolding)
/// </summary>
public sealed class IdentityAppDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<IdentityAppDbContext>
{
    /// <inheritdoc />
    public IdentityAppDbContext CreateDbContext(string[] args)
    {
        string provider = Environment.GetEnvironmentVariable("EF_IDENTITY_PROVIDER")
            ?? "SqlServer";

        string connection = Environment.GetEnvironmentVariable("EF_IDENTITY_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=ImpiTrack.Identity.Design;Trusted_Connection=True;";

        var optionsBuilder = new DbContextOptionsBuilder<IdentityAppDbContext>();

        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseNpgsql(connection, npgsql =>
                npgsql.MigrationsAssembly("ImpiTrack.Auth.Infrastructure"));
        }
        else
        {
            optionsBuilder.UseSqlServer(connection, sql =>
                sql.MigrationsAssembly("ImpiTrack.Auth.Infrastructure"));
        }

        return new IdentityAppDbContext(optionsBuilder.Options);
    }
}
