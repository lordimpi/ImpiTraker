using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Connection;
using ImpiTrack.DataAccess.InMemory;
using ImpiTrack.Shared.Options;
using ImpiTrack.DataAccess.Migrations;
using ImpiTrack.DataAccess.Repositories;
using ImpiTrack.Ops;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiTrack.DataAccess.Extensions;

/// <summary>
/// Extensiones de DI para registrar persistencia SQL o fallback en memoria.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra la capa de acceso a datos y migraciones de acuerdo con la configuracion.
    /// </summary>
    /// <param name="services">Coleccion de servicios.</param>
    /// <param name="configuration">Raiz de configuracion de la aplicacion.</param>
    /// <param name="registerMigrationHostedService">Indica si se registra el hosted service de migraciones.</param>
    /// <returns>Coleccion original para encadenamiento.</returns>
    public static IServiceCollection AddImpiTrackDataAccess(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerMigrationHostedService = true)
    {
        services.AddImpiTrackOptionsCore();

        services
            .BindOptions<DatabaseOptions>(configuration, DatabaseOptions.SectionName)
            .Validate(
                static options =>
                    DatabaseProviderParser.TryParse(options.Provider, out _),
                "Database:Provider debe ser InMemory, SqlServer o Postgres.");

        services.AddSingleton<DatabaseRuntimeContext>();

        DatabaseOptions options = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        DatabaseProvider provider = DatabaseProviderParser.Parse(options.Provider);
        string? resolvedConnectionString = ProviderConnectionStringResolver.ResolveDatabaseConnectionString(
            configuration,
            provider,
            options.ConnectionString);

        bool sqlEnabled = provider is DatabaseProvider.SqlServer or DatabaseProvider.Postgres;
        if (sqlEnabled && string.IsNullOrWhiteSpace(resolvedConnectionString))
        {
            throw new InvalidOperationException(
                $"database_connection_string_missing provider={provider} keys=Database:ConnectionString|ConnectionStrings:SqlServer|ConnectionStrings:Postgres");
        }

        if (sqlEnabled)
        {
            services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
            services.AddSingleton<SqlDataRepository>();
            services.AddSingleton<IIngestionRepository>(sp => sp.GetRequiredService<SqlDataRepository>());
            services.AddSingleton<IOpsRepository>(sp => sp.GetRequiredService<SqlDataRepository>());
            services.AddSingleton<IUserAccountRepository>(sp => sp.GetRequiredService<SqlDataRepository>());
            services.AddSingleton<ITelemetryQueryRepository>(sp => sp.GetRequiredService<SqlDataRepository>());
            services.AddSingleton<IMigrationRunner, SqlScriptMigrationRunner>();
        }
        else
        {
            services.AddSingleton<IOpsDataStore, InMemoryOpsDataStore>();
            services.AddSingleton<InMemoryDataRepository>();
            services.AddSingleton<IIngestionRepository>(sp => sp.GetRequiredService<InMemoryDataRepository>());
            services.AddSingleton<IOpsRepository>(sp => sp.GetRequiredService<InMemoryDataRepository>());
            services.AddSingleton<IUserAccountRepository>(sp => sp.GetRequiredService<InMemoryDataRepository>());
            services.AddSingleton<ITelemetryQueryRepository>(sp => sp.GetRequiredService<InMemoryDataRepository>());
            services.AddSingleton<IMigrationRunner, NoOpMigrationRunner>();
        }

        if (registerMigrationHostedService)
        {
            services.AddHostedService<DatabaseMigrationHostedService>();
        }

        return services;
    }
}
