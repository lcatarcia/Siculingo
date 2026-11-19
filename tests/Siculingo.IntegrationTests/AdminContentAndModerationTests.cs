using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Administration;
using Siculingo.Api.Features.Learning;
using Siculingo.Infrastructure.Editorial;

namespace Siculingo.IntegrationTests;

public sealed class AdminContentAndModerationTests : IAsyncLifetime
{
    private const string Password = "Una frase di prova 2026!";
    private static readonly Guid Lesson1Id = Guid.Parse("5a100001-0000-4000-8000-000000000001");

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2027, 4, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly DatabaseApiFactory factory;
    private PilotDraft pilotDraft = null!;

    public AdminContentAndModerationTests()
    {
        factory = new DatabaseApiFactory(clock);
    }

    public async Task InitializeAsync()
    {
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        var approvedJson = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "pilot-greetings.v1.json"));
        pilotDraft = PilotDraft.Parse(approvedJson);
        await PilotDraftImporter.PublishApprovedAsync(db, approvedJson);
    }

    public async Task DisposeAsync()
    {
        await factory.DeleteTestDatabaseAsync();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Role_boundaries_separate_students_editors_and_admins_and_cli_bootstrap_is_idempotent()
    {
        using var studentClient = Client();
        using var editorClient = Client();
        using var adminClient = Client();

        await RegisterAsync(studentClient, "studente_base");
        await RegisterAsync(editorClient, "redattore_ct");
        await RegisterAsync(adminClient, "revisore_admin");

        // Student cannot access any admin endpoint
        using var studentDenied = await studentClient.GetAsync("/api/v1/admin/overview");
        Assert.Equal(HttpStatusCode.Forbidden, studentDenied.StatusCode);

        // Bootstrap Editor and Admin via CLI service method (idempotent when run twice)
        await GrantRoleViaCliAsync("redattore_ct:Editor");
        await GrantRoleViaCliAsync("revisore_admin:Admin");
        await GrantRoleViaCliAsync("revisore_admin:Admin");

        // Re-login so auth cookies pick up updated role claims after security-stamp bump
        var editorAccount = await LoginAsync(editorClient, "redattore_ct");
        var adminAccount = await LoginAsync(adminClient, "revisore_admin");
        Assert.Contains("Editor", editorAccount.Roles!);
        Assert.Contains("Admin", adminAccount.Roles!);

        // Editor can read overview/catalog and create drafts + lexemes, but gets 403 on reviewer/admin actions
        var editorOverview = await editorClient.GetFromJsonAsync<AdminOverviewResponse>("/api/v1/admin/overview");
        Assert.NotNull(editorOverview);
        Assert.Equal("editor", editorOverview.AccessLevel);

        var editorCatalog = await editorClient.GetFromJsonAsync<EditorialCatalogResponse>("/api/v1/admin/catalog");
        Assert.NotNull(editorCatalog);
        var targetUnitId = editorCatalog.Courses[0].Sections[0].Units[0].Id;

        using var createdDraftResp = await PostCsrfAsync(
            editorClient,
            $"/api/v1/admin/units/{targetUnitId}/lessons",
            new CreateLessonDraftRequest(
                "Bozza editoriale non approvata",
                "Verificare il blocco pubblicazione senza revisione linguistica",
                "Da verificare con parlante catanese"));
        createdDraftResp.EnsureSuccessStatusCode();
        var createdLesson = await createdDraftResp.Content.ReadFromJsonAsync<EditorialLessonSummaryDto>();
        Assert.NotNull(createdLesson);
        Assert.False(createdLesson.IsPublished);
        Assert.Equal("draft", createdLesson.EditorialState);

        using var editorReviewDenied = await PostCsrfAsync(
            editorClient,
            $"/api/v1/admin/lessons/{createdLesson.Id}/review",
            new ReviewLessonRequest(true, "Tentativo non autorizzato"));
        Assert.Equal(HttpStatusCode.Forbidden, editorReviewDenied.StatusCode);

        using var editorPublishDenied = await PostCsrfAsync(
            editorClient,
            $"/api/v1/admin/lessons/{createdLesson.Id}/publish",
            new { });
        Assert.Equal(HttpStatusCode.Forbidden, editorPublishDenied.StatusCode);

        using var editorUsersDenied = await editorClient.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, editorUsersDenied.StatusCode);

        using var editorReportsDenied = await editorClient.GetAsync("/api/v1/admin/reports");
        Assert.Equal(HttpStatusCode.Forbidden, editorReportsDenied.StatusCode);

        // Admin cannot publish before linguistic approval either (409 Conflict)
        using var unapprovedPublish = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/lessons/{createdLesson.Id}/publish",
            new { });
        Assert.Equal(HttpStatusCode.Conflict, unapprovedPublish.StatusCode);

        // Editor submits for review -> Admin approves -> Admin publishes
        using var submitResp = await PostCsrfAsync(
            editorClient,
            $"/api/v1/admin/lessons/{createdLesson.Id}/submit-review",
            new SubmitReviewRequest("Pronta per controllo linguistico"));
        submitResp.EnsureSuccessStatusCode();

        using var approveResp = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/lessons/{createdLesson.Id}/review",
            new ReviewLessonRequest(true, "Approvata dal revisore"));
        approveResp.EnsureSuccessStatusCode();

        using var publishResp = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/lessons/{createdLesson.Id}/publish",
            new { });
        publishResp.EnsureSuccessStatusCode();
        var publishedSummary = await publishResp.Content.ReadFromJsonAsync<EditorialLessonSummaryDto>();
        Assert.NotNull(publishedSummary);
        Assert.True(publishedSummary.IsPublished);
        Assert.Equal("approved", publishedSummary.EditorialState);
    }

    [Fact]
    public async Task Editing_and_republishing_lesson_never_mutates_active_student_attempt_snapshot()
    {
        using var studentClient = Client();
        using var adminClient = Client();

        await RegisterAsync(studentClient, "studente_in_corso");
        await RegisterAsync(adminClient, "admin_catalogo");
        await GrantRoleViaCliAsync("admin_catalogo:Admin");
        await LoginAsync(adminClient, "admin_catalogo");

        // Student starts Lesson 1 and answers exercise 1
        using var startResp = await PostCsrfAsync(
            studentClient,
            $"/api/v1/learning/lessons/{Lesson1Id}/attempts",
            new StartLessonAttemptRequest(false));
        startResp.EnsureSuccessStatusCode();
        var initialAttempt = await startResp.Content.ReadFromJsonAsync<LessonAttemptResponse>();
        Assert.NotNull(initialAttempt);
        var originalTitle = initialAttempt.LessonTitle;

        var lesson1Draft = pilotDraft.Lessons.Single(l => l.Id == Lesson1Id);
        var firstAnswer = BuildCorrectAnswer(lesson1Draft.Exercises[0]);
        using var step1Resp = await PostCsrfAsync(
            studentClient,
            $"/api/v1/learning/attempts/{initialAttempt.AttemptId}/answers",
            new SubmitExerciseAnswerRequest(lesson1Draft.Exercises[0].Id, firstAnswer));
        step1Resp.EnsureSuccessStatusCode();

        // Admin edits Lesson 1 draft title & objective -> resets editorialState to draft while live lesson stays untouched
        var detail = await adminClient.GetFromJsonAsync<EditorialLessonDetailResponse>($"/api/v1/admin/lessons/{Lesson1Id}");
        Assert.NotNull(detail);
        Assert.NotNull(detail.Draft);

        var updatedDraft = detail.Draft with
        {
            Title = "Primi saluti a Catania (edizione rivista)",
            Objective = "Salutare con naturalezza catanese (versione aggiornata)"
        };
        var updatedJson = PilotDraftImporter.SerializeLesson(updatedDraft);

        using var saveDraftResp = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/lessons/{Lesson1Id}/draft",
            new SaveLessonDraftRequest(updatedJson, "mbare / 'mbare", "Revisione interna"));
        saveDraftResp.EnsureSuccessStatusCode();
        var afterSave = await saveDraftResp.Content.ReadFromJsonAsync<EditorialLessonDetailResponse>();
        Assert.NotNull(afterSave);
        Assert.Equal("draft", afterSave.Metadata.EditorialState);
        Assert.True(afterSave.Metadata.HasUnpublishedChanges);
        Assert.Equal(originalTitle, afterSave.Metadata.PublishedOrCurrentTitle);

        // Republishing before re-approval is rejected with 409
        using var blockedRepublish = await PostCsrfAsync(adminClient, $"/api/v1/admin/lessons/{Lesson1Id}/publish", new { });
        Assert.Equal(HttpStatusCode.Conflict, blockedRepublish.StatusCode);

        // Re-approve and republish Lesson 1
        using var reapprove = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/lessons/{Lesson1Id}/review",
            new ReviewLessonRequest(true, "Nuovo titolo approvato"));
        reapprove.EnsureSuccessStatusCode();

        using var republish = await PostCsrfAsync(adminClient, $"/api/v1/admin/lessons/{Lesson1Id}/publish", new { });
        republish.EnsureSuccessStatusCode();

        // Student's in-progress attempt STILL retains its original immutable snapshot and completes cleanly
        var resumedAttempt = await studentClient.GetFromJsonAsync<LessonAttemptResponse>(
            $"/api/v1/learning/attempts/{initialAttempt.AttemptId}");
        Assert.NotNull(resumedAttempt);
        Assert.Equal(originalTitle, resumedAttempt.LessonTitle);

        for (var i = 1; i < lesson1Draft.Exercises.Count; i++)
        {
            var ex = lesson1Draft.Exercises[i];
            using var ansResp = await PostCsrfAsync(
                studentClient,
                $"/api/v1/learning/attempts/{initialAttempt.AttemptId}/answers",
                new SubmitExerciseAnswerRequest(ex.Id, BuildCorrectAnswer(ex)));
            ansResp.EnsureSuccessStatusCode();
        }

        using var completeResp = await PostCsrfAsync(
            studentClient,
            $"/api/v1/learning/attempts/{initialAttempt.AttemptId}/complete",
            new { });
        completeResp.EnsureSuccessStatusCode();
        var completion = await completeResp.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        Assert.NotNull(completion);
        Assert.Equal(originalTitle, completion.LessonTitle);
        Assert.Equal(100, completion.AccuracyPercent);

        // Meanwhile, the learning path now reflects the newly published title for future attempts
        var path = await studentClient.GetFromJsonAsync<LearningPathResponse>("/api/v1/learning/path");
        Assert.NotNull(path);
        var pathLesson1 = path.Courses[0].Sections[0].Units[0].Lessons.Single(l => l.Id == Lesson1Id);
        Assert.Equal("Primi saluti a Catania (edizione rivista)", pathLesson1.Title);
    }

    [Fact]
    public async Task Lexicon_reports_user_suspension_and_audit_trail_work_end_to_end()
    {
        using var studentClient = Client();
        using var adminClient = Client();

        var studentReg = await RegisterAsync(studentClient, "utente_segnalatore");
        var adminReg = await RegisterAsync(adminClient, "admin_moderatore");
        await GrantRoleViaCliAsync("admin_moderatore:Admin");
        await LoginAsync(adminClient, "admin_moderatore");

        // Student reports an exercise issue
        using var reportResp = await PostCsrfAsync(
            studentClient,
            "/api/v1/reports/exercises",
            new CreateExerciseReportRequest(Lesson1Id, "l1-01", "variante", "Si sente spesso anche la grafia 'mbare con apostrofo."));
        reportResp.EnsureSuccessStatusCode();
        var createdReport = await reportResp.Content.ReadFromJsonAsync<ReportCreatedResponse>();
        Assert.NotNull(createdReport);
        Assert.Equal("open", createdReport.Status);

        // Admin creates and approves a catanese lexicon entry
        using var lexemeResp = await PostCsrfAsync(
            adminClient,
            "/api/v1/admin/lexemes",
            new UpsertLinguisticLexemeRequest(
                null,
                "Mbare",
                "Amico, compare",
                "Appellativo caloroso usato nel parlato quotidiano catanese.",
                "colloquiale",
                "'mbare, cumpà",
                "Uso urbano di Catania",
                true));
        lexemeResp.EnsureSuccessStatusCode();
        var lexeme = await lexemeResp.Content.ReadFromJsonAsync<LinguisticLexemeDto>();
        Assert.NotNull(lexeme);
        Assert.Equal("inReview", lexeme.ReviewState);

        using var approveLexemeResp = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/lexemes/{lexeme.Id}/review",
            new ReviewLexemeRequest(true, "Grafia e varianti confermate"));
        approveLexemeResp.EnsureSuccessStatusCode();

        // Admin resolves the student report
        using var resolveResp = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/reports/{createdReport.ReportId}/resolve",
            new ResolveUserReportRequest("resolved", "Variante registrata nell'archivio lessicale."));
        resolveResp.EnsureSuccessStatusCode();

        // Self-protection: Admin cannot suspend themselves or strip their own Admin role
        using var selfSuspend = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/users/{adminReg.Account.Id}/suspension",
            new SetUserSuspensionRequest(true, "Auto-blocco"));
        Assert.Equal(HttpStatusCode.Conflict, selfSuspend.StatusCode);

        using var selfDemote = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/users/{adminReg.Account.Id}/roles",
            new UpdateUserRoleRequest("Admin", false));
        Assert.Equal(HttpStatusCode.Conflict, selfDemote.StatusCode);

        // Suspending the student immediately invalidates their active cookie (401) and blocks login (403)
        using var suspendResp = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/users/{studentReg.Account.Id}/suspension",
            new SetUserSuspensionRequest(true, "Abuso segnalazioni ripetute"));
        suspendResp.EnsureSuccessStatusCode();

        using var studentMeAfterSuspend = await studentClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, studentMeAfterSuspend.StatusCode);

        using var blockedLogin = await PostAuthAsync(studentClient, "login", new CredentialsRequest("utente_segnalatore", Password));
        Assert.Equal(HttpStatusCode.Forbidden, blockedLogin.StatusCode);

        // Reinstating allows login again
        using var reinstateResp = await PostCsrfAsync(
            adminClient,
            $"/api/v1/admin/users/{studentReg.Account.Id}/suspension",
            new SetUserSuspensionRequest(false, null));
        reinstateResp.EnsureSuccessStatusCode();

        var reinstatedAccount = await LoginAsync(studentClient, "utente_segnalatore");
        Assert.Equal("utente_segnalatore", reinstatedAccount.Username);

        // Verify audit log recorded key administrative operations
        var audit = await adminClient.GetFromJsonAsync<AdminAuditListResponse>("/api/v1/admin/audit");
        Assert.NotNull(audit);
        Assert.Contains(audit.Items, item => item.ActionType == "lexeme.approved");
        Assert.Contains(audit.Items, item => item.ActionType == "report.resolved");
        Assert.Contains(audit.Items, item => item.ActionType == "user.suspended");
        Assert.Contains(audit.Items, item => item.ActionType == "user.reinstated");
    }

    private async Task GrantRoleViaCliAsync(string rawArg) =>
        await AdministrationEndpoints.GrantRoleCliAsync(factory.Services, rawArg, NullLogger.Instance);

    private static SubmittedExerciseAnswer BuildCorrectAnswer(ExerciseDraft exercise) => exercise switch
    {
        ChoiceDraft choice => new ChoiceAnswerSubmission(choice.CorrectOptionIndex),
        FillBlankDraft fill => new FillBlankAnswerSubmission(fill.CorrectOptionIndex),
        TranslationDraft tr => new GuidedTranslationAnswerSubmission(tr.AnswerTokens),
        MatchingDraft match => new MatchingAnswerSubmission(
            match.Pairs.Select(p => new MatchingPairInput(p.Left, p.Right)).ToList()),
        _ => throw new InvalidOperationException("Unsupported exercise draft")
    };

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static async Task<RecoveryResponse> RegisterAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "register", new RegisterAccountRequest(username, Password, true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecoveryResponse>())!;
    }

    private static async Task<AccountResponse> LoginAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "login", new CredentialsRequest(username, Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private static async Task<HttpResponseMessage> PostAuthAsync<TBody>(HttpClient client, string route, TBody body) =>
        await PostCsrfAsync(client, $"/api/v1/auth/{route}", body);

    private static async Task<HttpResponseMessage> PostCsrfAsync<TBody>(HttpClient client, string path, TBody body)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        return await client.SendAsync(request);
    }

    private sealed class FakeTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset nowUtc = initialUtc;
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
