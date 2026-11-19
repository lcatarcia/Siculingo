using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Learning;
using Siculingo.Infrastructure.Editorial;

namespace Siculingo.IntegrationTests;

public sealed class LessonAttemptTests : IAsyncLifetime
{
    private const string Password = "Una frase di prova 2026!";
    private static readonly Guid Lesson1Id = Guid.Parse("5a100001-0000-4000-8000-000000000001");
    private static readonly Guid Lesson2Id = Guid.Parse("5a100002-0000-4000-8000-000000000002");
    private static readonly Guid Lesson3Id = Guid.Parse("5a100003-0000-4000-8000-000000000003");

    private readonly DatabaseApiFactory factory = new();

    public async Task InitializeAsync()
    {
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        var approvedJson = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "pilot-greetings.v1.json"));
        await PilotDraftImporter.PublishApprovedAsync(db, approvedJson);
    }

    public async Task DisposeAsync()
    {
        await factory.DeleteTestDatabaseAsync();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Student_completes_lesson_across_all_four_formats_survives_reload_and_unlocks_next_lesson()
    {
        using var client = Client();
        await RegisterAsync(client, "studente_catania");

        var initialPath = await client.GetFromJsonAsync<LearningPathResponse>("/api/v1/learning/path");
        Assert.NotNull(initialPath);
        var lessons = initialPath.Courses.Single().Sections.Single().Units.Single().Lessons;
        Assert.Equal(5, lessons.Count);
        Assert.Equal(["available", "locked", "locked", "locked", "locked"], lessons.Select(item => item.Status));

        // Locked lesson cannot be started directly
        using var lockedStart = await PostLearningAsync(client, $"/api/v1/learning/lessons/{Lesson2Id}/attempts", new StartLessonAttemptRequest());
        Assert.Equal(HttpStatusCode.Forbidden, lockedStart.StatusCode);

        // Start lesson 1 and inspect raw JSON for solution leaks
        using var startResponse = await PostLearningAsync(client, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest());
        startResponse.EnsureSuccessStatusCode();
        var rawAttemptJson = await startResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctOptionIndex", rawAttemptJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerTokens", rawAttemptJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewNote", rawAttemptJson, StringComparison.OrdinalIgnoreCase);

        var attempt = JsonSerializer.Deserialize<LessonAttemptResponse>(rawAttemptJson, PilotDraft.JsonOptions);
        Assert.NotNull(attempt);
        Assert.Equal(8, attempt.TotalExercises);
        Assert.Equal(0, attempt.CurrentStepIndex);

        // Matching exercise rightItems are shuffled and distinct from pairs
        var matchingDto = Assert.IsType<StudentMatchingExercise>(attempt.Exercises[2]);
        Assert.Equal(["Ciau", "Bona sira", "Bona notti"], matchingDto.LeftItems);
        Assert.NotEqual(["Ciao", "Buonasera", "Buonanotte"], matchingDto.RightItems);

        // Submit first 4 exercises (2 choice + 2 matching), including 1 intentional wrong choice
        var step0 = await SubmitAsync(client, attempt.AttemptId, "saluti-01", new ChoiceAnswerSubmission(1)); // Ciau -> correct
        Assert.True(step0.IsCorrect);

        var step1 = await SubmitAsync(client, attempt.AttemptId, "saluti-02", new ChoiceAnswerSubmission(0)); // Bona notti -> wrong
        Assert.False(step1.IsCorrect);
        Assert.Equal("Bongiornu", step1.ExpectedDisplayAnswer);

        var step2 = await SubmitAsync(client, attempt.AttemptId, "saluti-03", new MatchingAnswerSubmission(
        [
            new MatchingPairInput("Ciau", "Ciao"),
            new MatchingPairInput("Bona sira", "Buonasera"),
            new MatchingPairInput("Bona notti", "Buonanotte")
        ]));
        Assert.True(step2.IsCorrect);

        var step3 = await SubmitAsync(client, attempt.AttemptId, "saluti-04", new MatchingAnswerSubmission(
        [
            new MatchingPairInput("Bongiornu", "Incontro al mattino"),
            new MatchingPairInput("A prestu", "Ci rivedremo presto"),
            new MatchingPairInput("Bona notti", "Congedo prima di dormire")
        ]));
        Assert.True(step3.IsCorrect);

        // Simulate page reload: GET path shows activeAttempt at 4/8, GET attempt restores recordedAnswers
        var midPath = await client.GetFromJsonAsync<LearningPathResponse>("/api/v1/learning/path");
        Assert.NotNull(midPath?.ActiveAttempt);
        Assert.Equal(attempt.AttemptId, midPath.ActiveAttempt.AttemptId);
        Assert.Equal(4, midPath.ActiveAttempt.AnsweredCount);

        var reloaded = await client.GetFromJsonAsync<LessonAttemptResponse>($"/api/v1/learning/attempts/{attempt.AttemptId}", PilotDraft.JsonOptions);
        Assert.NotNull(reloaded);
        Assert.Equal(4, reloaded.CurrentStepIndex);
        Assert.Equal(4, reloaded.RecordedAnswers.Count);

        // Submit remaining 4 exercises (2 guidedTranslation + 2 fillBlank)
        var step4 = await SubmitAsync(client, attempt.AttemptId, "saluti-05", new GuidedTranslationAnswerSubmission(["Bona", "sira"]));
        Assert.True(step4.IsCorrect);

        var step5 = await SubmitAsync(client, attempt.AttemptId, "saluti-06", new GuidedTranslationAnswerSubmission(["A", "prestu"]));
        Assert.True(step5.IsCorrect);

        var step6 = await SubmitAsync(client, attempt.AttemptId, "saluti-07", new FillBlankAnswerSubmission(1));
        Assert.True(step6.IsCorrect);

        var step7 = await SubmitAsync(client, attempt.AttemptId, "saluti-08", new FillBlankAnswerSubmission(2));
        Assert.True(step7.IsCorrect);
        Assert.True(step7.IsReadyToComplete);

        // Complete attempt (and retry complete to ensure idempotency)
        using var complete1 = await PostLearningAsync(client, $"/api/v1/learning/attempts/{attempt.AttemptId}/complete", new { });
        using var complete2 = await PostLearningAsync(client, $"/api/v1/learning/attempts/{attempt.AttemptId}/complete", new { });
        complete1.EnsureSuccessStatusCode();
        complete2.EnsureSuccessStatusCode();

        var summary = await complete1.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        Assert.NotNull(summary);
        Assert.Equal(7, summary.CorrectAnswers);
        Assert.Equal(8, summary.TotalExercises);
        Assert.Equal(88, summary.AccuracyPercent);
        Assert.Equal(Lesson2Id, summary.NextUnlockedLessonId);

        await using (var db = factory.CreateDbContext())
        {
            var savedCompletion = await db.UserLessonCompletions.SingleAsync();
            Assert.Equal(1, savedCompletion.CompletedCount);
        }

        // Verify path now marks Lesson 1 completed, Lesson 2 available, Lesson 3 locked
        var afterPath = await client.GetFromJsonAsync<LearningPathResponse>("/api/v1/learning/path");
        Assert.NotNull(afterPath);
        Assert.Null(afterPath.ActiveAttempt);
        var updatedLessons = afterPath.Courses.Single().Sections.Single().Units.Single().Lessons;
        Assert.Equal(["completed", "available", "locked", "locked", "locked"], updatedLessons.Select(item => item.Status));

        // Lesson 2 can now be started, while Lesson 3 remains locked
        using var startLesson2 = await PostLearningAsync(client, $"/api/v1/learning/lessons/{Lesson2Id}/attempts", new StartLessonAttemptRequest());
        startLesson2.EnsureSuccessStatusCode();
        using var lockedLesson3 = await PostLearningAsync(client, $"/api/v1/learning/lessons/{Lesson3Id}/attempts", new StartLessonAttemptRequest());
        Assert.Equal(HttpStatusCode.Forbidden, lockedLesson3.StatusCode);
    }

    [Fact]
    public async Task Concurrent_retries_do_not_duplicate_attempts_or_answers_and_snapshot_isolates_in_progress_attempt()
    {
        using var client1 = Client();
        using var client2 = Client();
        await RegisterAsync(client1, "concorrenza_ct");
        using var login2 = await PostAuthAsync(client2, "login", new CredentialsRequest("concorrenza_ct", Password));
        login2.EnsureSuccessStatusCode();

        // Concurrent start requests on the same lesson return the single in-progress attempt
        var startResults = await Task.WhenAll(
            PostLearningAsync(client1, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest()),
            PostLearningAsync(client2, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest()));
        try
        {
            Assert.All(startResults, res => res.EnsureSuccessStatusCode());
            var attemptA = await startResults[0].Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
            var attemptB = await startResults[1].Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
            Assert.NotNull(attemptA);
            Assert.NotNull(attemptB);
            Assert.Equal(attemptA.AttemptId, attemptB.AttemptId);

            // Simulate an editorial republication changing Lesson 1's PublishedSnapshotJson mid-attempt
            await using (var db = factory.CreateDbContext())
            {
                var lessonEntity = await db.Lessons.SingleAsync(lesson => lesson.Id == Lesson1Id);
                lessonEntity.Title = "Titolo modificato dopo avvio";
                lessonEntity.PublishedSnapshotJson = lessonEntity.PublishedSnapshotJson!
                    .Replace("Quale proposta significa ciao?", "Domanda cambiata in corsa", StringComparison.Ordinal);
                await db.SaveChangesAsync();
            }

            // Concurrent submission of the same exercise records exactly one answer and preserves original snapshot
            var answerTasks = await Task.WhenAll(
                PostLearningAsync(client1, $"/api/v1/learning/attempts/{attemptA.AttemptId}/answers",
                    new SubmitExerciseAnswerRequest("saluti-01", new ChoiceAnswerSubmission(1))),
                PostLearningAsync(client2, $"/api/v1/learning/attempts/{attemptA.AttemptId}/answers",
                    new SubmitExerciseAnswerRequest("saluti-01", new ChoiceAnswerSubmission(1))));
            try
            {
                Assert.All(answerTasks, res => res.EnsureSuccessStatusCode());
                await using var db = factory.CreateDbContext();
                Assert.Equal(1, await db.LessonAttemptAnswers.CountAsync(ans => ans.LessonAttemptId == attemptA.AttemptId));
            }
            finally
            {
                foreach (var res in answerTasks) res.Dispose();
            }

            var resumed = await client1.GetFromJsonAsync<LessonAttemptResponse>(
                $"/api/v1/learning/attempts/{attemptA.AttemptId}", PilotDraft.JsonOptions);
            Assert.NotNull(resumed);
            Assert.Equal("Ciao e arrivederci", resumed.LessonTitle);
            Assert.Equal("Quale proposta significa ciao?", resumed.Exercises[0].Prompt);
        }
        finally
        {
            foreach (var res in startResults) res.Dispose();
        }
    }

    [Fact]
    public async Task Foreign_attempts_and_missing_csrf_are_rejected_and_abandon_allows_fresh_attempt()
    {
        using var alice = Client();
        using var mallory = Client();
        await RegisterAsync(alice, "alice_etnea");
        await RegisterAsync(mallory, "mallory_etnea");

        using var started = await PostLearningAsync(alice, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest());
        started.EnsureSuccessStatusCode();
        var attempt = await started.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        Assert.NotNull(attempt);

        // Missing CSRF is rejected with 403
        using var noCsrf = await alice.PostAsJsonAsync(
            $"/api/v1/learning/attempts/{attempt.AttemptId}/answers",
            new SubmitExerciseAnswerRequest("saluti-01", new ChoiceAnswerSubmission(1)),
            PilotDraft.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);

        // Mallory cannot read, answer, complete, or abandon Alice's attempt
        Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/v1/learning/attempts/{attempt.AttemptId}")).StatusCode);
        using var malloryAnswer = await PostLearningAsync(mallory, $"/api/v1/learning/attempts/{attempt.AttemptId}/answers",
            new SubmitExerciseAnswerRequest("saluti-01", new ChoiceAnswerSubmission(1)));
        Assert.Equal(HttpStatusCode.NotFound, malloryAnswer.StatusCode);
        using var malloryAbandon = await PostLearningAsync(mallory, $"/api/v1/learning/attempts/{attempt.AttemptId}/abandon", new { });
        Assert.Equal(HttpStatusCode.NotFound, malloryAbandon.StatusCode);

        // Invalid payload shape (e.g. choice payload on matching step or out-of-order exerciseId) is rejected
        using var wrongOrder = await PostLearningAsync(alice, $"/api/v1/learning/attempts/{attempt.AttemptId}/answers",
            new SubmitExerciseAnswerRequest("saluti-03", new ChoiceAnswerSubmission(0)));
        Assert.Equal(HttpStatusCode.Conflict, wrongOrder.StatusCode);

        using var wrongType = await PostLearningAsync(alice, $"/api/v1/learning/attempts/{attempt.AttemptId}/answers",
            new SubmitExerciseAnswerRequest("saluti-01", new GuidedTranslationAnswerSubmission(["Ciau"])));
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);

        // Alice abandons the attempt and starts a brand-new attempt cleanly
        using var abandoned = await PostLearningAsync(alice, $"/api/v1/learning/attempts/{attempt.AttemptId}/abandon", new { });
        Assert.Equal(HttpStatusCode.NoContent, abandoned.StatusCode);

        using var restarted = await PostLearningAsync(alice, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest());
        restarted.EnsureSuccessStatusCode();
        var fresh = await restarted.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        Assert.NotNull(fresh);
        Assert.NotEqual(attempt.AttemptId, fresh.AttemptId);
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "register", new RegisterAccountRequest(username, Password, true));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<ExerciseSubmissionResponse> SubmitAsync(
        HttpClient client, Guid attemptId, string exerciseId, SubmittedExerciseAnswer answer)
    {
        using var response = await PostLearningAsync(
            client,
            $"/api/v1/learning/attempts/{attemptId}/answers",
            new SubmitExerciseAnswerRequest(exerciseId, answer));
        response.EnsureSuccessStatusCode();
        var parsed = await response.Content.ReadFromJsonAsync<ExerciseSubmissionResponse>();
        Assert.NotNull(parsed);
        return parsed;
    }

    private static async Task<HttpResponseMessage> PostAuthAsync(HttpClient client, string action, object body)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        Assert.NotNull(csrf);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/{action}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostLearningAsync<T>(HttpClient client, string url, T body)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        Assert.NotNull(csrf);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: PilotDraft.JsonOptions)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }
}
