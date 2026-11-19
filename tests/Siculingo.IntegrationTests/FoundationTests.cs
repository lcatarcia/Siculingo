using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Siculingo.Domain.Learning;

namespace Siculingo.IntegrationTests;

public sealed class FoundationTests : IAsyncLifetime
{
    private readonly DatabaseApiFactory factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migrated_database_is_ready()
    {
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Existing_database_without_migration_history_is_not_ready()
    {
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("application/problem+json", ready.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Migration_enforces_case_insensitive_unique_course_codes()
    {
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        db.Courses.Add(new Course { Code = "ct-base", Name = "Prima bozza" });
        await db.SaveChangesAsync();
        db.Courses.Add(new Course { Code = "CT-BASE", Name = "Seconda bozza" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Unknown_api_route_returns_problem_details()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/unknown");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, problem?.Status);
    }

    public async Task DisposeAsync()
    {
        await factory.DeleteTestDatabaseAsync();
        await factory.DisposeAsync();
    }
}
