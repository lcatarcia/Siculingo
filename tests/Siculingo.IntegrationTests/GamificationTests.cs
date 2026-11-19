using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Gamification;
using Siculingo.Api.Features.Learning;
using Siculingo.Infrastructure.Editorial;

namespace Siculingo.IntegrationTests;

public sealed class GamificationTests : IAsyncLifetime
{
    private const string Password = "Una frase di prova 2026!";
    private static readonly Guid Lesson1Id = Guid.Parse("5a100001-0000-4000-8000-000000000001");
    private static readonly Guid Lesson2Id = Guid.Parse("5a100002-0000-4000-8000-000000000002");
    private static readonly Guid Lesson3Id = Guid.Parse("5a100003-0000-4000-8000-000000000003");
    private static readonly Guid Lesson4Id = Guid.Parse("5a100004-0000-4000-8000-000000000004");
    private static readonly Guid Lesson5Id = Guid.Parse("5a100005-0000-4000-8000-000000000005");

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2027, 3, 27, 10, 0, 0, TimeSpan.Zero));
    private readonly DatabaseApiFactory factory;
    private PilotDraft pilotDraft = null!;

    public GamificationTests()
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
    public async Task Concurrent_completion_credits_xp_once_and_reconciles_user_stats_with_ledger()
    {
        using var clientA = Client();
        using var clientB = Client();
        await RegisterAsync(clientA, "etna_concurrent");
        using var loginB = await PostAuthAsync(clientB, "login", new CredentialsRequest("etna_concurrent", Password));
        loginB.EnsureSuccessStatusCode();

        var attempt = await StartAndAnswerAllAsync(clientA, Lesson1Id, allCorrect: true);

        // Fire two concurrent completion requests across separate sessions
        var completionTaskA = PostLearningAsync(clientA, $"/api/v1/learning/attempts/{attempt.AttemptId}/complete", new { });
        var completionTaskB = PostLearningAsync(clientB, $"/api/v1/learning/attempts/{attempt.AttemptId}/complete", new { });
        using var respA = await completionTaskA;
        using var respB = await completionTaskB;
        respA.EnsureSuccessStatusCode();
        respB.EnsureSuccessStatusCode();

        var bodyA = await respA.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        var bodyB = await respB.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        Assert.NotNull(bodyA);
        Assert.NotNull(bodyB);

        // 8 correct * 2 = 16 + 10 first completion + 5 perfect + 5 first activity of day = 36 lesson XP
        // + 10 (daily-lesson-1) + 10 (daily-xp-25) = 56 XP total
        Assert.Equal(56, bodyA.Rewards.XpEarned);
        Assert.Equal(56, bodyB.Rewards.XpEarned);
        Assert.Equal(56, bodyA.Rewards.TotalXp);
        Assert.Equal(1, bodyA.Rewards.CurrentStreak);
        Assert.Contains(bodyA.Rewards.NewlyUnlockedAchievements, item => item.Code == "first-lesson");
        Assert.Contains(bodyA.Rewards.NewlyUnlockedAchievements, item => item.Code == "perfect-lesson");
        Assert.Contains(bodyA.Rewards.NewlyCompletedMissions, item => item.Code == "daily-lesson-1");
        Assert.Contains(bodyA.Rewards.NewlyCompletedMissions, item => item.Code == "daily-xp-25");

        var summary = await clientA.GetFromJsonAsync<GamificationSummaryResponse>("/api/v1/gamification/summary");
        Assert.NotNull(summary);
        Assert.Equal(56, summary.TotalXp);
        Assert.True(summary.IsReconciledWithLedger);
        Assert.Equal(56, summary.TodayXp);
        Assert.Equal(56, summary.WeeklyXp);
        Assert.Equal(1, summary.CurrentStreak);
        Assert.Equal(1, summary.UniqueLessonsCompleted);
        Assert.Equal(1, summary.TotalLessonCompletions);
        Assert.Equal(8, summary.ConsecutiveCorrectAnswers);

        await using var db = factory.CreateDbContext();
        var ledgerSum = await db.XpLedger.SumAsync(item => item.Amount);
        Assert.Equal(56, ledgerSum);
        Assert.Equal(2, await db.UserAchievements.CountAsync());
    }

    [Fact]
    public async Task Repeat_completions_enforce_daily_review_cap_without_per_exercise_farming()
    {
        using var client = Client();
        await RegisterAsync(client, "ripasso_onesto");

        // 1st completion of Lesson 1 (with 1 wrong answer -> 7*2=14 + 10 + 5 first today = 29 XP + 20 daily missions = 49 XP)
        var first = await PlayAndCompleteLessonAsync(client, Lesson1Id, allCorrect: false);
        Assert.Equal(49, first.Rewards.XpEarned);
        Assert.False(first.Rewards.DailyReviewCapReached);

        // Repeat #1 on same day, imperfect -> +5 review XP + 10 XP (daily-correct-10 reached at 14 correct) = 15 XP
        var review1 = await PlayAndCompleteLessonAsync(client, Lesson1Id, allCorrect: false);
        Assert.Equal(15, review1.Rewards.XpEarned);
        Assert.False(review1.Rewards.DailyReviewCapReached);

        // Repeat #2 on same day, perfect -> +5 review XP + 5 perfect bonus = 10 XP
        var review2 = await PlayAndCompleteLessonAsync(client, Lesson1Id, allCorrect: true);
        Assert.Equal(10, review2.Rewards.XpEarned);
        Assert.False(review2.Rewards.DailyReviewCapReached);

        // Repeat #3 on same day, imperfect -> +5 review XP + 25 XP (weekly-lessons-4 reached) = 30 XP
        var review3 = await PlayAndCompleteLessonAsync(client, Lesson1Id, allCorrect: false);
        Assert.Equal(30, review3.Rewards.XpEarned);
        Assert.False(review3.Rewards.DailyReviewCapReached);

        // Repeat #4 on same day, imperfect -> daily review cap reached -> 0 XP
        var review4 = await PlayAndCompleteLessonAsync(client, Lesson1Id, allCorrect: false);
        Assert.Equal(0, review4.Rewards.XpEarned);
        Assert.True(review4.Rewards.DailyReviewCapReached);

        var summary = await client.GetFromJsonAsync<GamificationSummaryResponse>("/api/v1/gamification/summary");
        Assert.NotNull(summary);
        Assert.Equal(104, summary.TotalXp);
        Assert.Equal(3, summary.RewardedReviewsToday);
        Assert.Equal(1, summary.UniqueLessonsCompleted);
        Assert.Equal(5, summary.TotalLessonCompletions);
        Assert.True(summary.IsReconciledWithLedger);
    }

    [Fact]
    public async Task Streak_handles_midnight_spring_dst_skipped_day_and_unit_completion_unlocks_level_two_and_trophies()
    {
        using var client = Client();
        await RegisterAsync(client, "costanza_ora_legale");

        // Day 1: Saturday 2027-03-27 22:30 UTC -> 2027-03-27 23:30 Europe/Rome (CET, UTC+1) -> 36 + 20 = 56 XP
        clock.SetUtcNow(new DateTimeOffset(2027, 3, 27, 22, 30, 0, TimeSpan.Zero));
        var d1 = await PlayAndCompleteLessonAsync(client, Lesson1Id, allCorrect: true);
        Assert.Equal(1, d1.Rewards.CurrentStreak);

        // Day 2 across Europe/Rome midnight & spring DST jump:
        // 2027-03-27 23:15 UTC -> 2027-03-28 00:15 Europe/Rome (still UTC date 27, but Rome date 28!)
        // 36 lesson XP + 20 daily missions + 25 weekly-perfect-2 = 81 XP -> Total 137 XP -> Level 2!
        clock.SetUtcNow(new DateTimeOffset(2027, 3, 27, 23, 15, 0, TimeSpan.Zero));
        var d2 = await PlayAndCompleteLessonAsync(client, Lesson2Id, allCorrect: true);
        Assert.Equal(2, d2.Rewards.CurrentStreak);
        Assert.True(d2.Rewards.LeveledUp);
        Assert.Equal(2, d2.Rewards.CurrentLevel);
        // 16 consecutive correct answers so far -> precision-10 unlocked on Lesson 2!
        Assert.Contains(d2.Rewards.NewlyUnlockedAchievements, item => item.Code == "precision-10");

        // Later on Sunday 2027-03-28 after DST switch (CEST, UTC+2): 2027-03-28 21:30 UTC -> 23:30 Rome (same local day 28)
        clock.SetUtcNow(new DateTimeOffset(2027, 3, 28, 21, 30, 0, TimeSpan.Zero));
        var d2Second = await PlayAndCompleteLessonAsync(client, Lesson3Id, allCorrect: true);
        Assert.Equal(2, d2Second.Rewards.CurrentStreak); // Same Rome day -> stays 2, no second daily bonus
        Assert.Equal(41, d2Second.Rewards.XpEarned); // 31 lesson XP + 10 daily-correct-10
        Assert.Equal(2, d2Second.Rewards.CurrentLevel);

        // Day 3: Monday 2027-03-29 08:00 UTC -> 10:00 CEST -> Streak 3 + streak-3 trophy!
        clock.SetUtcNow(new DateTimeOffset(2027, 3, 29, 8, 0, 0, TimeSpan.Zero));
        var d3 = await PlayAndCompleteLessonAsync(client, Lesson4Id, allCorrect: true);
        Assert.Equal(3, d3.Rewards.CurrentStreak);
        Assert.Contains(d3.Rewards.NewlyUnlockedAchievements, item => item.Code == "streak-3");

        // Skip Tuesday 2027-03-30 completely.
        // Before playing on Wednesday 2027-03-31, summary must already report effective CurrentStreak = 0 while preserving BestStreak = 3.
        clock.SetUtcNow(new DateTimeOffset(2027, 3, 31, 9, 0, 0, TimeSpan.Zero));
        var beforePlaySummary = await client.GetFromJsonAsync<GamificationSummaryResponse>("/api/v1/gamification/summary");
        Assert.NotNull(beforePlaySummary);
        Assert.Equal(0, beforePlaySummary.CurrentStreak);
        Assert.Equal(3, beforePlaySummary.BestStreak);
        Assert.False(beforePlaySummary.StudiedToday);

        // Complete Lesson 5 on Wednesday 2027-03-31 -> streak restarts at 1, BestStreak stays 3, pilot-unit-complete unlocks (5 of 6 trophies, streak-7 still locked)!
        var d5 = await PlayAndCompleteLessonAsync(client, Lesson5Id, allCorrect: true);
        Assert.Equal(1, d5.Rewards.CurrentStreak);
        Assert.Equal(3, d5.Rewards.BestStreak);
        Assert.Contains(d5.Rewards.NewlyUnlockedAchievements, item => item.Code == "pilot-unit-complete");

        var catalog = await client.GetFromJsonAsync<AchievementsCatalogResponse>("/api/v1/gamification/achievements");
        Assert.NotNull(catalog);
        Assert.Equal(5, catalog.UnlockedCount);
        Assert.Equal(6, catalog.TotalCount);
    }

    private async Task<LessonCompletionResponse> PlayAndCompleteLessonAsync(HttpClient client, Guid lessonId, bool allCorrect)
    {
        var attempt = await StartAndAnswerAllAsync(client, lessonId, allCorrect);
        using var completeResp = await PostLearningAsync(client, $"/api/v1/learning/attempts/{attempt.AttemptId}/complete", new { });
        completeResp.EnsureSuccessStatusCode();
        var body = await completeResp.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        return Assert.IsType<LessonCompletionResponse>(body);
    }

    private async Task<LessonAttemptResponse> StartAndAnswerAllAsync(HttpClient client, Guid lessonId, bool allCorrect)
    {
        using var startResp = await PostLearningAsync(client, $"/api/v1/learning/lessons/{lessonId}/attempts", new StartLessonAttemptRequest());
        startResp.EnsureSuccessStatusCode();
        var attempt = await startResp.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        Assert.NotNull(attempt);

        var draftLesson = pilotDraft.Lessons.Single(item => item.Id == lessonId);
        for (var i = 0; i < draftLesson.Exercises.Count; i++)
        {
            var draftExercise = draftLesson.Exercises[i];
            var makeWrong = !allCorrect && i == 0;
            SubmittedExerciseAnswer payload = draftExercise switch
            {
                ChoiceDraft choice => new ChoiceAnswerSubmission(
                    makeWrong ? (choice.CorrectOptionIndex + 1) % choice.Options.Count : choice.CorrectOptionIndex),
                MatchingDraft matching => new MatchingAnswerSubmission(
                    makeWrong
                        ? matching.Pairs.Select((pair, idx) =>
                            new MatchingPairInput(pair.Left, matching.Pairs[(idx + 1) % matching.Pairs.Count].Right)).ToList()
                        : matching.Pairs.Select(pair => new MatchingPairInput(pair.Left, pair.Right)).ToList()),
                TranslationDraft guided => new GuidedTranslationAnswerSubmission(
                    makeWrong ? [.. guided.Tokens] : [.. guided.AnswerTokens]),
                FillBlankDraft fill => new FillBlankAnswerSubmission(
                    makeWrong ? (fill.CorrectOptionIndex + 1) % fill.Options.Count : fill.CorrectOptionIndex),
                _ => throw new InvalidOperationException("Unsupported exercise draft.")
            };

            using var submitResp = await PostLearningAsync(
                client,
                $"/api/v1/learning/attempts/{attempt.AttemptId}/answers",
                new SubmitExerciseAnswerRequest(draftExercise.Id, payload));
            submitResp.EnsureSuccessStatusCode();
        }

        return attempt;
    }

    private HttpClient Client() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "register", new RegisterAccountRequest(username, Password, true));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> PostAuthAsync<T>(HttpClient client, string path, T body)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/{path}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostLearningAsync<T>(HttpClient client, string path, T body)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: PilotDraft.JsonOptions)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        return await client.SendAsync(request);
    }

    private sealed class FakeTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset currentUtc = initialUtc;
        public override DateTimeOffset GetUtcNow() => currentUtc;
        public void SetUtcNow(DateTimeOffset value) => currentUtc = value;
    }
}
