using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Administration;
using Siculingo.Infrastructure.Editorial;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Common;

public static class OperationalCli
{
    public static async Task<bool> TryExecuteAsync(
        WebApplication app,
        IConfiguration configuration,
        string[] args)
    {
        if (configuration["import-pilot"] is { } draftPilotPath)
        {
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Pilot draft import (--import-pilot) is allowed only in Development, never in the published application.");
            }

            await using var scope = app.Services.CreateAsyncScope();
            var changed = await PilotDraftImporter.ImportAsync(
                scope.ServiceProvider.GetRequiredService<SiculingoDbContext>(),
                await File.ReadAllTextAsync(draftPilotPath));
            app.Logger.LogInformation(
                "Pilot import: {Result}. All content remains unpublished.",
                changed ? "five draft lessons saved" : "identical draft lessons already present");
            return true;
        }

        if (HasSwitch(configuration, args, "migrate-only"))
        {
            await ExecuteWithSqlWakeupRetryAsync(app.Logger, "database migration", async () =>
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SiculingoDbContext>();
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                app.Logger.LogInformation("Applying {Count} pending EF Core migration(s)...", pending.Count);
                await db.Database.MigrateAsync();
                app.Logger.LogInformation("Database schema is up to date.");
            });
            return true;
        }

        var approvedPilotPath = configuration["seed-approved-pilot"] ?? configuration["publish-pilot"];
        if (!string.IsNullOrWhiteSpace(approvedPilotPath))
        {
            if (configuration["publish-pilot"] is not null && !app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Use --seed-approved-pilot in non-Development controlled deployment steps.");
            }

            var json = await File.ReadAllTextAsync(approvedPilotPath);
            await ExecuteWithSqlWakeupRetryAsync(app.Logger, "approved pilot seed", async () =>
            {
                await using var scope = app.Services.CreateAsyncScope();
                var changed = await PilotDraftImporter.PublishApprovedAsync(
                    scope.ServiceProvider.GetRequiredService<SiculingoDbContext>(),
                    json);
                app.Logger.LogInformation(
                    "Approved pilot publication: {Result}.",
                    changed ? "approved pilot unit published on catalog" : "approved pilot unit already up to date");
            });
            return true;
        }

        var grantRoleSpec = configuration["promote-role"] ?? configuration["grant-role"];
        if (!string.IsNullOrWhiteSpace(grantRoleSpec))
        {
            if (configuration["grant-role"] is not null && !app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Use --promote-role \"<username>:<Editor|Admin>\" for controlled operational role bootstrap outside Development.");
            }

            await ExecuteWithSqlWakeupRetryAsync(app.Logger, "role bootstrap", async () =>
            {
                await AdministrationEndpoints.GrantRoleCliAsync(app.Services, grantRoleSpec, app.Logger);
            });
            return true;
        }

        return false;
    }

    private static bool HasSwitch(IConfiguration configuration, string[] args, string key)
    {
        if (string.Equals(configuration[key], "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var flag = $"--{key}";
        return args.Any(arg =>
            string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith($"{flag}=", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ExecuteWithSqlWakeupRetryAsync(
        ILogger logger,
        string operationName,
        Func<Task> action)
    {
        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientSqlWakeupError(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Min(15, attempt * 5));
                logger.LogWarning(
                    ex,
                    "Transient SQL availability error during {Operation} (attempt {Attempt}/{MaxAttempts}, Azure SQL Free Offer may be resuming from auto-pause). Retrying in {DelaySeconds}s...",
                    operationName,
                    attempt,
                    maxAttempts,
                    delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
    }

    private static bool IsTransientSqlWakeupError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlEx && sqlEx.Number is 40613 or 40197 or 40501 or 49918 or 49919 or 49920 or -2)
            {
                return true;
            }
        }

        return false;
    }
}
