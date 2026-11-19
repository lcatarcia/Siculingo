using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Siculingo.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection("ConnectionStrings"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Database),
                "Configure ConnectionStrings:Database with user-secrets or environment variables. Run tools\\setup-local.ps1 for local development.")
            .ValidateOnStart();

        services.AddDbContext<SiculingoDbContext>((provider, options) =>
            options.UseSqlServer(
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Database,
                sql => sql.MigrationsAssembly(typeof(SiculingoDbContext).Assembly.FullName)));

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(
            "database", tags: ["ready"]);
        return services;
    }
}
