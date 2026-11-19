using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Learning;
using Siculingo.Domain.Learning;
using Siculingo.Infrastructure.Editorial;

namespace Siculingo.IntegrationTests;

public sealed class AccountTests : IAsyncLifetime
{
    private const string Password = "Una frase di prova 2026!";
    private readonly MutableClock clock = new();
    private readonly DatabaseApiFactory factory;

    public AccountTests() => factory = new DatabaseApiFactory(clock);

    public async Task InitializeAsync()
    {
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task Registration_persists_hashed_credentials_and_cookie_expires_after_absolute_thirty_days()
    {
        using var client = Client();
        using var response = await Post(client, "register", new RegisterAccountRequest("PrimoUtente", Password, true));
        response.EnsureSuccessStatusCode();
        var registration = await response.Content.ReadFromJsonAsync<RecoveryResponse>();
        Assert.NotNull(registration);
        var cookie = SetCookieHeaderValue.Parse(response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("Siculingo.Auth=")));
        Assert.True(cookie.HttpOnly);
        Assert.NotNull(cookie.Expires);
        Assert.InRange(Math.Abs((cookie.Expires.Value - clock.GetUtcNow().AddDays(30)).TotalSeconds), 0, 1);
        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync();
        Assert.NotEqual(Password, user.PasswordHash);
        var recovery = await db.AccountRecoveryCodes.SingleAsync();
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(registration.RecoveryCode)), recovery.CodeHash);
        Assert.Null(user.Email);

        clock.Advance(TimeSpan.FromDays(29));
        using var stillValid = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
        Assert.False(stillValid.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", stillValid.Headers.CacheControl?.ToString());
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_case_variant_registrations_create_only_one_account()
    {
        using var first = Client();
        using var second = Client();
        var responses = await Task.WhenAll(
            Post(first, "register", new RegisterAccountRequest("StessoNome", Password, true)),
            Post(second, "register", new RegisterAccountRequest("stessonome", Password, true)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await using var db = factory.CreateDbContext();
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(1, await db.AccountRecoveryCodes.CountAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Mutations_require_csrf_and_anonymous_path_does_not_redirect()
    {
        using var client = Client();
        using var missing = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterAccountRequest("NoCsrf", Password, true));
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        using var anonymous = await client.GetAsync("/api/v1/learning/path");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Null(anonymous.Headers.Location);
        Assert.Equal("application/problem+json", anonymous.Content.Headers.ContentType?.MediaType);
        await Register(client);
        using var logout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.Forbidden, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Recovery_rotates_code_unlocks_account_and_immediately_revokes_all_devices()
    {
        using var first = Client();
        using var second = Client();
        using var recovering = Client();
        var registration = await Register(first);
        using var login = await Post(second, "login", new CredentialsRequest("utente_test", Password));
        login.EnsureSuccessStatusCode();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var invalid = await Post(recovering, "login", new CredentialsRequest("Utente_Test", "password sbagliata"));
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }
        using var locked = await Post(recovering, "login", new CredentialsRequest("Utente_Test", Password));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        const string nextPassword = "Una nuova frase dopo il recupero!";
        using var recovered = await Post(recovering, "recover",
            new RecoveryRequest("Utente_Test", registration.RecoveryCode, nextPassword));
        recovered.EnsureSuccessStatusCode();
        var replacement = await recovered.Content.ReadFromJsonAsync<RecoveryResponse>();
        Assert.NotNull(replacement);
        Assert.NotEqual(registration.RecoveryCode, replacement.RecoveryCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/auth/me")).StatusCode);
        using var oldLogin = await Post(recovering, "login", new CredentialsRequest("Utente_Test", Password));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await Post(recovering, "login", new CredentialsRequest("Utente_Test", nextPassword));
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        using var replay = await Post(recovering, "recover",
            new RecoveryRequest("Utente_Test", registration.RecoveryCode, "Non deve diventare la password!"));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Recovery_code_can_be_consumed_only_once_by_concurrent_requests()
    {
        using var owner = Client();
        using var first = Client();
        using var second = Client();
        var registration = await Register(owner);
        var responses = await Task.WhenAll(
            Post(first, "recover", new RecoveryRequest("Utente_Test", registration.RecoveryCode, "Password di recupero primo!")),
            Post(second, "recover", new RecoveryRequest("Utente_Test", registration.RecoveryCode, "Password di recupero secondo!")));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            var winner = await responses.Single(response => response.IsSuccessStatusCode).Content.ReadFromJsonAsync<RecoveryResponse>();
            Assert.NotNull(winner);
            await using var db = factory.CreateDbContext();
            Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(winner.RecoveryCode)),
                (await db.AccountRecoveryCodes.SingleAsync()).CodeHash);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Invalid_recovery_does_not_consume_code_or_reveal_account_existence()
    {
        using var owner = Client();
        using var recovering = Client();
        var registration = await Register(owner);
        using var weakPassword = await Post(recovering, "recover",
            new RecoveryRequest("Utente_Test", registration.RecoveryCode, "short"));
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        using var wrongCode = await Post(recovering, "recover",
            new RecoveryRequest("Utente_Test", "invalid-code", Password));
        using var unknownAccount = await Post(recovering, "recover",
            new RecoveryRequest("Unknown_User", "invalid-code", Password));
        var known = await wrongCode.Content.ReadFromJsonAsync<ProblemDetails>();
        var unknown = await unknownAccount.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(HttpStatusCode.BadRequest, wrongCode.StatusCode);
        Assert.Equal(wrongCode.StatusCode, unknownAccount.StatusCode);
        Assert.NotNull(known);
        Assert.NotNull(unknown);
        Assert.Equal(known.Title, unknown.Title);
        Assert.Equal(known.Detail, unknown.Detail);
        using var valid = await Post(recovering, "recover",
            new RecoveryRequest("Utente_Test", registration.RecoveryCode, "Una nuova password valida!"));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task Credential_rate_limit_returns_problem_details_and_retry_after()
    {
        using var client = Client();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var invalid = await Post(client, "login", new CredentialsRequest("Unknown_User", Password));
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }
        using var limited = await Post(client, "login", new CredentialsRequest("Unknown_User", Password));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Logout_current_browser_preserves_other_device_and_logout_all_revokes_both()
    {
        using var first = Client();
        using var second = Client();
        await Register(first);
        using var login = await Post(second, "login", new CredentialsRequest("Utente_Test", Password));
        login.EnsureSuccessStatusCode();
        using var logout = await Post(first, "logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/auth/me")).StatusCode);
        using var again = await Post(first, "login", new CredentialsRequest("Utente_Test", Password));
        again.EnsureSuccessStatusCode();
        using var all = await Post(first, "logout-all", new { });
        Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Display_name_is_persisted_across_devices_without_changing_login_or_renewing_cookie()
    {
        using var first = Client();
        using var second = Client();
        var account = await Register(first);
        Assert.Equal(account.Account.Username, account.Account.DisplayName);
        using var login = await Post(second, "login", new CredentialsRequest("Utente_Test", Password));
        login.EnsureSuccessStatusCode();
        using var update = await Post(first, "profile", new ProfileRequest("  Ficodì <amico> 🌵  "));
        update.EnsureSuccessStatusCode();
        Assert.False(update.Headers.Contains("Set-Cookie"));
        var profile = await second.GetFromJsonAsync<AccountResponse>("/api/v1/auth/me");
        Assert.NotNull(profile);
        Assert.Equal("Ficodì <amico> 🌵", profile.DisplayName);
        Assert.Equal("Utente_Test", profile.Username);
        Assert.Equal(account.Account.Id, profile.Id);
        using var reset = await Post(first, "profile", new ProfileRequest("   "));
        reset.EnsureSuccessStatusCode();
        var restored = await second.GetFromJsonAsync<AccountResponse>("/api/v1/auth/me");
        Assert.Equal("Utente_Test", restored?.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Nome\nsu due righe")]
    [InlineData("Un nome che supera certamente i quaranta caratteri consentiti")]
    public async Task Invalid_display_name_does_not_change_profile(string? name)
    {
        using var client = Client();
        await Register(client);
        using var invalid = await Post(client, "profile", new { displayName = name });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var unchanged = await client.GetFromJsonAsync<AccountResponse>("/api/v1/auth/me");
        Assert.Equal("Utente_Test", unchanged?.DisplayName);
    }

    [Fact]
    public async Task Settings_require_authorization_and_csrf_without_changing_credentials()
    {
        using var anonymous = Client();
        using var denied = await Post(anonymous, "profile", new ProfileRequest("Non autorizzato"));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var deniedPassword = await Post(anonymous, "password", new ChangePasswordRequest(Password, "Una nuova frase valida!"));
        Assert.Equal(HttpStatusCode.Unauthorized, deniedPassword.StatusCode);
        using var client = Client();
        await Register(client);
        using var missingProfile = await client.PostAsJsonAsync("/api/v1/auth/profile", new ProfileRequest("Non salvato"));
        using var missingPassword = await client.PostAsJsonAsync("/api/v1/auth/password",
            new ChangePasswordRequest(Password, "Una nuova frase valida!"));
        Assert.Equal(HttpStatusCode.Forbidden, missingProfile.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingPassword.StatusCode);
        var profile = await client.GetFromJsonAsync<AccountResponse>("/api/v1/auth/me");
        Assert.Equal("Utente_Test", profile?.DisplayName);
    }

    [Fact]
    public async Task Password_change_revokes_every_session_but_preserves_recovery_code_and_display_name()
    {
        using var first = Client();
        using var second = Client();
        var registration = await Register(first);
        using var profile = await Post(first, "profile", new ProfileRequest("Ficodì"));
        profile.EnsureSuccessStatusCode();
        using var login = await Post(second, "login", new CredentialsRequest("Utente_Test", Password));
        login.EnsureSuccessStatusCode();
        using var wrong = await Post(first, "password", new ChangePasswordRequest("password errata", "Una nuova frase valida!"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var shortPassword = await Post(first, "password", new ChangePasswordRequest(Password, "short"));
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        using var same = await Post(first, "password", new ChangePasswordRequest(Password, Password));
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/auth/me")).StatusCode);
        const string nextPassword = "La nuova password del profilo!";
        using var updated = await Post(first, "password", new ChangePasswordRequest(Password, nextPassword));
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/auth/me")).StatusCode);
        using var oldLogin = await Post(first, "login", new CredentialsRequest("Utente_Test", Password));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await Post(first, "login", new CredentialsRequest("Utente_Test", nextPassword));
        newLogin.EnsureSuccessStatusCode();
        Assert.Equal("Ficodì", (await first.GetFromJsonAsync<AccountResponse>("/api/v1/auth/me"))?.DisplayName);
        using var recovery = await Post(second, "recover",
            new RecoveryRequest("Utente_Test", registration.RecoveryCode, "Password recuperata successivamente!"));
        recovery.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Imported_pilot_is_idempotent_unpublished_and_not_exposed_in_student_responses()
    {
        var approvedJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pilot-greetings.v1.json"));
        var draftNode = System.Text.Json.Nodes.JsonNode.Parse(approvedJson)!;
        draftNode["reviewStatus"] = PilotDraft.PendingReviewStatus;
        var json = draftNode.ToJsonString();
        await using var db = factory.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PilotDraftImporter.ImportAsync(db, approvedJson));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PilotDraftImporter.PublishApprovedAsync(db, json));
        Assert.True(await PilotDraftImporter.ImportAsync(db, json));
        db.ChangeTracker.Clear();
        Assert.False(await PilotDraftImporter.ImportAsync(db, json));
        Assert.Equal(5, await db.Lessons.CountAsync());
        Assert.Equal(0, await db.Lessons.CountAsync(lesson => lesson.IsPublished));
        var saved = await db.Lessons.OrderBy(lesson => lesson.SortOrder).ToListAsync();
        foreach (var lesson in saved)
        {
            Assert.False(string.IsNullOrEmpty(lesson.EditorialDraftJson));
            Assert.Null(lesson.PublishedSnapshotJson);
        }
        using var client = Client();
        await Register(client);
        using var response = await client.GetAsync("/api/v1/learning/path");
        response.EnsureSuccessStatusCode();
        var path = await response.Content.ReadFromJsonAsync<LearningPathResponse>();
        Assert.NotNull(path);
        Assert.Empty(path.Courses);
        Assert.DoesNotContain("correctOptionIndex", await response.Content.ReadAsStringAsync());
        var changed = json.Replace("Ciao e arrivederci", "Titolo cambiato");
        await Assert.ThrowsAsync<InvalidOperationException>(() => PilotDraftImporter.ImportAsync(db, changed));
        db.ChangeTracker.Clear();
        Assert.Contains(await db.Lessons.ToListAsync(), lesson => lesson.Title == "Ciao e arrivederci");
        Assert.True(await PilotDraftImporter.PublishApprovedAsync(db, approvedJson));
        db.ChangeTracker.Clear();
        Assert.False(await PilotDraftImporter.PublishApprovedAsync(db, approvedJson));
        Assert.Equal(5, await db.Lessons.CountAsync(lesson => lesson.IsPublished && lesson.PublishedSnapshotJson != null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PilotDraftImporter.ImportAsync(db, json));
    }

    [Fact]
    public async Task Published_path_is_ordered_and_drafts_at_every_level_are_hidden()
    {
        const string validSnapshot = """
            {"id":"5a100001-0000-4000-8000-000000000001","title":"Demo","objective":"Obj","reviewNote":"Note","exercises":[
            {"kind":"choice","id":"e1","prompt":"p","explanation":"x","options":["a","b"],"correctOptionIndex":0},
            {"kind":"choice","id":"e2","prompt":"p","explanation":"x","options":["a","b"],"correctOptionIndex":0},
            {"kind":"choice","id":"e3","prompt":"p","explanation":"x","options":["a","b"],"correctOptionIndex":0},
            {"kind":"choice","id":"e4","prompt":"p","explanation":"x","options":["a","b"],"correctOptionIndex":0},
            {"kind":"choice","id":"e5","prompt":"p","explanation":"x","options":["a","b"],"correctOptionIndex":0},
            {"kind":"choice","id":"e6","prompt":"p","explanation":"x","options":["a","b"],"correctOptionIndex":0},
            {"kind":"choice","id":"e7","prompt":"p","explanation":"x","options":["a","b"],"correctOptionIndex":0},
            {"kind":"choice","id":"e8","prompt":"p","explanation":"x","options":["a","b"],"correctOptionIndex":0}]}
            """;
        await using var db = factory.CreateDbContext();
        db.Courses.Add(new Course
        {
            Code = "test-published", Name = "Percorso pubblicato", IsPublished = true,
            Sections =
            [
                new CourseSection
                {
                    Title = "Sezione pubblicata", IsPublished = true,
                    Units =
                    [
                        new LearningUnit
                        {
                            Title = "Unita pubblicata", IsPublished = true,
                            Lessons =
                            [
                                new Lesson { Title = "Seconda", SortOrder = 2, IsPublished = true, PublishedSnapshotJson = validSnapshot },
                                new Lesson { Title = "Prima", SortOrder = 1, IsPublished = true, PublishedSnapshotJson = validSnapshot },
                                new Lesson { Title = "Lezione bozza", SortOrder = 3 },
                                new Lesson { Title = "Senza snapshot", SortOrder = 4, IsPublished = true }
                            ]
                        },
                        new LearningUnit { Title = "Unita bozza", SortOrder = 1,
                            Lessons = [new Lesson { Title = "Nascosta da unita", IsPublished = true, PublishedSnapshotJson = validSnapshot }] }
                    ]
                },
                new CourseSection { Title = "Sezione bozza", SortOrder = 1,
                    Units = [new LearningUnit { Title = "Nascosta da sezione", IsPublished = true }] }
            ]
        });
        db.Courses.Add(new Course { Code = "test-draft", Name = "Corso bozza",
            Sections = [new CourseSection { Title = "Nascosta da corso", IsPublished = true }] });
        await db.SaveChangesAsync();
        using var client = Client();
        await Register(client);
        var path = await client.GetFromJsonAsync<LearningPathResponse>("/api/v1/learning/path");
        Assert.NotNull(path);
        var course = Assert.Single(path.Courses);
        var section = Assert.Single(course.Sections);
        var unit = Assert.Single(section.Units);
        Assert.Equal(["Prima", "Seconda"], unit.Lessons.Select(lesson => lesson.Title));
        Assert.Equal(["available", "locked"], unit.Lessons.Select(lesson => lesson.Status));
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> Post(HttpClient client, string action, object data)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        Assert.NotNull(csrf);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/{action}")
        {
            Content = JsonContent.Create(data)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }

    private static async Task<RecoveryResponse> Register(HttpClient client)
    {
        using var response = await Post(client, "register", new RegisterAccountRequest("Utente_Test", Password, true));
        response.EnsureSuccessStatusCode();
        var registration = await response.Content.ReadFromJsonAsync<RecoveryResponse>();
        Assert.NotNull(registration);
        return registration;
    }

    public async Task DisposeAsync()
    {
        await factory.DeleteTestDatabaseAsync();
        await factory.DisposeAsync();
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
