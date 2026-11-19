using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Siculingo.Api.Common;

public sealed record HealthResponse(string Status);

public static class HealthEndpoints
{
    public static void MapServiceHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => TypedResults.Ok(new HealthResponse("Healthy")))
            .WithName("GetLiveness")
            .WithTags("Health");

        endpoints.MapGet("/health/ready", async Task<Results<Ok<HealthResponse>, ProblemHttpResult>> (
            HealthCheckService checks, CancellationToken cancellationToken) =>
        {
            var report = await checks.CheckHealthAsync(
                registration => registration.Tags.Contains("ready"),
                cancellationToken);
            return report.Status == HealthStatus.Healthy
                ? TypedResults.Ok(new HealthResponse("Healthy"))
                : TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Servizio non pronto.",
                    detail: "Verificare il database e le migrazioni nei log del servizio.");
        })
            .WithName("GetReadiness")
            .WithTags("Health")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
