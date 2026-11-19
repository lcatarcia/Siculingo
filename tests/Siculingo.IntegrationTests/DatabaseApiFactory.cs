using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.IntegrationTests;

public sealed class DatabaseApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;
    private readonly TimeProvider? clock;
    private readonly IReadOnlyDictionary<string, string?>? extraConfiguration;
    private readonly string environmentName;
    private readonly string? webRootPath;

    public string ConnectionString => connectionString;

    public DatabaseApiFactory(
        TimeProvider? clock = null,
        IReadOnlyDictionary<string, string?>? extraConfiguration = null,
        string environmentName = "Testing",
        string? webRootPath = null,
        string? existingConnectionString = null)
    {
        this.clock = clock;
        this.extraConfiguration = extraConfiguration;
        this.environmentName = environmentName;
        this.webRootPath = webRootPath;
        if (!string.IsNullOrWhiteSpace(existingConnectionString))
        {
            var existingBuilder = new SqlConnectionStringBuilder(existingConnectionString);
            if (!existingBuilder.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("These integration tests require a dedicated local LocalDB instance.");
            }
            connectionString = existingBuilder.ConnectionString;
            return;
        }

        var local = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .Build();
        var configured = Environment.GetEnvironmentVariable("SICULINGO_TEST_DATABASE")
            ?? local.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Run tools\\setup-local.ps1 before SQL integration tests.");
        var connection = new SqlConnectionStringBuilder(configured);
        if (!connection.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("These integration tests require a dedicated local LocalDB instance.");
        }
        connection.InitialCatalog = $"Siculingo_Test_{Guid.NewGuid():N}";
        connectionString = connection.ConnectionString;
    }

    public SiculingoDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<SiculingoDbContext>()
            .UseSqlServer(connectionString)
            .Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        if (!string.IsNullOrWhiteSpace(webRootPath))
        {
            builder.UseWebRoot(webRootPath);
        }
        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
            if (clock is not null)
            {
                services.AddSingleton(clock);
                services.ConfigureApplicationCookie(options => options.TimeProvider = clock);
            }
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = connectionString,
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning",
                ["Logging:LogLevel:Microsoft.AspNetCore.Antiforgery"] = "Error"
            };
            if (extraConfiguration is not null)
            {
                foreach (var pair in extraConfiguration)
                {
                    values[pair.Key] = pair.Value;
                }
            }
            configuration.AddInMemoryCollection(values);
        });
    }

    public async Task DeleteTestDatabaseAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var header) &&
                    IPAddress.TryParse(header.ToString(), out var parsedIp))
                {
                    context.Connection.RemoteIpAddress = parsedIp;
                }
                return nextMiddleware();
            });
            next(app);
        };
    }
}
