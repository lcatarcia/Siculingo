using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Gamification;
using Siculingo.Api.Features.Learning;
using Siculingo.Api.Features.Social;
using Siculingo.Domain.Social;
using Siculingo.Infrastructure.Editorial;

namespace Siculingo.IntegrationTests;

public sealed class HeartsMissionsSocialAndLeagueTests : IAsyncLifetime
{
    private const string Password = "Una frase di prova 2026!";
    private static readonly Guid Lesson1Id = Guid.Parse("5a100001-0000-4000-8000-000000000001");
    private static readonly Guid Lesson2Id = Guid.Parse("5a100002-0000-4000-8000-000000000002");

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2027, 4, 5, 9, 0, 0, TimeSpan.Zero)); // Monday 2027-04-05 11:00 Europe/Rome
    private readonly DatabaseApiFactory factory;
    private PilotDraft pilotDraft = null!;

    public HeartsMissionsSocialAndLeagueTests()
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
    public async Task Hearts_consume_on_wrong_path_answers_allow_finishing_active_lesson_block_new_path_lesson_at_zero_restore_via_practice_and_regenerate_every_four_hours()
    {
        using var client = Client();
        await RegisterAsync(client, "cuori_catania");

        var initialHearts = await client.GetFromJsonAsync<HeartsStatusDto>("/api/v1/gamification/hearts");
        Assert.NotNull(initialHearts);
        Assert.Equal(5, initialHearts.CurrentHearts);
        Assert.Equal(5, initialHearts.MaxHearts);
        Assert.Null(initialHearts.SecondsUntilNextHeart);

        // Start Lesson 1 on the path and make 5 wrong answers (reaching 0 hearts mid-lesson), then finish the remaining 3 exercises
        using var start1 = await PostJsonAsync(client, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest());
        start1.EnsureSuccessStatusCode();
        var attempt1 = await start1.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        Assert.NotNull(attempt1);
        Assert.False(attempt1.IsPractice);

        var draft1 = pilotDraft.Lessons.Single(item => item.Id == Lesson1Id);
        for (var i = 0; i < draft1.Exercises.Count; i++)
        {
            var wrong = i < 5;
            var submission = await SubmitDraftExerciseAsync(client, attempt1.AttemptId, draft1.Exercises[i], makeWrong: wrong);
            var expectedHearts = Math.Max(0, 5 - (i + 1));
            if (wrong)
            {
                Assert.True(submission.HeartConsumed);
                Assert.Equal(expectedHearts, submission.Hearts.CurrentHearts);
            }
            else
            {
                Assert.False(submission.HeartConsumed);
                Assert.Equal(0, submission.Hearts.CurrentHearts);
            }
        }

        // Duplicate retry on an already-answered wrong exercise must NOT consume another heart
        var duplicateRetry = await SubmitDraftExerciseAsync(client, attempt1.AttemptId, draft1.Exercises[0], makeWrong: true);
        Assert.False(duplicateRetry.HeartConsumed);

        // Even at 0 hearts, the already-started path lesson can be completed normally and unlocks Lesson 2!
        using var complete1 = await PostJsonAsync(client, $"/api/v1/learning/attempts/{attempt1.AttemptId}/complete", new { });
        complete1.EnsureSuccessStatusCode();
        var done1 = await complete1.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        Assert.NotNull(done1);
        Assert.Equal(Lesson2Id, done1.NextUnlockedLessonId);
        Assert.Equal(0, done1.Rewards.Hearts.CurrentHearts);

        // Starting newly unlocked Lesson 2 with 0 hearts is blocked with 409 Conflict
        using var blockedStart2 = await PostJsonAsync(client, $"/api/v1/learning/lessons/{Lesson2Id}/attempts", new StartLessonAttemptRequest());
        Assert.Equal(HttpStatusCode.Conflict, blockedStart2.StatusCode);

        // Practice session IS allowed at 0 hearts, wrong answers in practice do NOT consume hearts, and completion restores +1 heart!
        using var startPractice = await PostJsonAsync(client, "/api/v1/learning/practice/start", new StartPracticeRequest());
        startPractice.EnsureSuccessStatusCode();
        var practiceAttempt = await startPractice.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        Assert.NotNull(practiceAttempt);
        Assert.True(practiceAttempt.IsPractice);

        var practiceDraft = pilotDraft.Lessons.Single(item => item.Id == practiceAttempt.LessonId);
        for (var i = 0; i < practiceDraft.Exercises.Count; i++)
        {
            var sub = await SubmitDraftExerciseAsync(client, practiceAttempt.AttemptId, practiceDraft.Exercises[i], makeWrong: i == 0);
            Assert.False(sub.HeartConsumed);
        }

        using var completePractice = await PostJsonAsync(client, $"/api/v1/learning/attempts/{practiceAttempt.AttemptId}/complete", new { });
        completePractice.EnsureSuccessStatusCode();
        var practiceDone = await completePractice.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        Assert.NotNull(practiceDone);
        Assert.True(practiceDone.IsPractice);
        Assert.True(practiceDone.Rewards.PracticeHeartRestored);
        Assert.Equal(1, practiceDone.Rewards.Hearts.CurrentHearts);

        // With 1 heart restored, Lesson 2 can now be started!
        using var allowedStart2 = await PostJsonAsync(client, $"/api/v1/learning/lessons/{Lesson2Id}/attempts", new StartLessonAttemptRequest());
        allowedStart2.EnsureSuccessStatusCode();

        // Advance clock by 8 hours -> +2 passive hearts regenerated (1 + 2 = 3 hearts)
        clock.Advance(TimeSpan.FromHours(8));
        var after8h = await client.GetFromJsonAsync<HeartsStatusDto>("/api/v1/gamification/hearts");
        Assert.NotNull(after8h);
        Assert.Equal(3, after8h.CurrentHearts);
        Assert.NotNull(after8h.SecondsUntilNextHeart);

        // Advance clock by another 10 hours -> capped at 5/5 hearts with null timer
        clock.Advance(TimeSpan.FromHours(10));
        var after18h = await client.GetFromJsonAsync<HeartsStatusDto>("/api/v1/gamification/hearts");
        Assert.NotNull(after18h);
        Assert.Equal(5, after18h.CurrentHearts);
        Assert.Null(after18h.SecondsUntilNextHeart);
    }

    [Fact]
    public async Task Practice_for_brand_new_user_at_zero_hearts_restores_heart_without_prematurely_completing_lesson_one()
    {
        using var client = Client();
        await RegisterAsync(client, "nuovo_zero_cuori");

        // Start Lesson 1, make 5 wrong answers -> 0 hearts, then abandon Lesson 1
        using var start1 = await PostJsonAsync(client, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest());
        start1.EnsureSuccessStatusCode();
        var attempt1 = await start1.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        Assert.NotNull(attempt1);

        var draft1 = pilotDraft.Lessons.Single(item => item.Id == Lesson1Id);
        for (var i = 0; i < 5; i++)
        {
            await SubmitDraftExerciseAsync(client, attempt1.AttemptId, draft1.Exercises[i], makeWrong: true);
        }

        using var abandonResp = await PostJsonAsync(client, $"/api/v1/learning/attempts/{attempt1.AttemptId}/abandon", new { });
        Assert.Equal(HttpStatusCode.NoContent, abandonResp.StatusCode);

        // Cannot start new Lesson 1 path attempt at 0 hearts
        using var blocked = await PostJsonAsync(client, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest());
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // Complete a practice session -> restores +1 heart, does NOT unlock Lesson 2!
        using var practiceStart = await PostJsonAsync(client, "/api/v1/learning/practice/start", new StartPracticeRequest());
        practiceStart.EnsureSuccessStatusCode();
        var practice = await practiceStart.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        Assert.NotNull(practice);

        foreach (var ex in draft1.Exercises)
        {
            await SubmitDraftExerciseAsync(client, practice.AttemptId, ex, makeWrong: false);
        }

        using var practiceComplete = await PostJsonAsync(client, $"/api/v1/learning/attempts/{practice.AttemptId}/complete", new { });
        practiceComplete.EnsureSuccessStatusCode();
        var summary = await practiceComplete.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        Assert.NotNull(summary);
        Assert.Equal(1, summary.Rewards.Hearts.CurrentHearts);
        Assert.Null(summary.NextUnlockedLessonId);

        var path = await client.GetFromJsonAsync<LearningPathResponse>("/api/v1/learning/path");
        var statuses = path!.Courses.Single().Sections.Single().Units.Single().Lessons.Select(l => l.Status).ToList();
        Assert.Equal(["available", "locked", "locked", "locked", "locked"], statuses);
    }

    [Fact]
    public async Task Hearts_consumption_feature_flag_disables_deduction_and_zero_heart_blocking()
    {
        await using var flagFactory = new DatabaseApiFactory(
            clock,
            new Dictionary<string, string?> { ["Hearts:ConsumptionEnabled"] = "false" });
        await using (var db = flagFactory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
            var approvedJson = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "pilot-greetings.v1.json"));
            await PilotDraftImporter.PublishApprovedAsync(db, approvedJson);
        }

        try
        {
            using var client = flagFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await RegisterAsync(client, "cuori_infiniti");

            using var start = await PostJsonAsync(client, $"/api/v1/learning/lessons/{Lesson1Id}/attempts", new StartLessonAttemptRequest());
            start.EnsureSuccessStatusCode();
            var attempt = await start.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
            Assert.NotNull(attempt);
            Assert.False(attempt.Hearts.ConsumptionEnabled);

            var draft1 = pilotDraft.Lessons.Single(item => item.Id == Lesson1Id);
            var wrongSub = await SubmitDraftExerciseAsync(client, attempt.AttemptId, draft1.Exercises[0], makeWrong: true);
            Assert.False(wrongSub.HeartConsumed);
            Assert.Equal(5, wrongSub.Hearts.CurrentHearts);
        }
        finally
        {
            await flagFactory.DeleteTestDatabaseAsync();
        }
    }

    [Fact]
    public async Task Missions_assign_without_duplicates_and_non_recursively_evaluate_daily_xp_target()
    {
        using var clientA = Client();
        using var clientB = Client();
        await RegisterAsync(clientA, "missioni_ct");
        using var loginB = await PostAuthAsync(clientB, "login", new CredentialsRequest("missioni_ct", Password));
        loginB.EnsureSuccessStatusCode();

        // Concurrent GET /missions must create exactly 5 rows (3 daily + 2 weekly) without duplicates
        var results = await Task.WhenAll(
            clientA.GetFromJsonAsync<MissionsOverviewResponse>("/api/v1/gamification/missions"),
            clientB.GetFromJsonAsync<MissionsOverviewResponse>("/api/v1/gamification/missions"));
        Assert.All(results, item =>
        {
            Assert.NotNull(item);
            Assert.Equal(3, item.DailyMissions.Count);
            Assert.Equal(2, item.WeeklyMissions.Count);
        });

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(5, await db.UserMissionProgress.CountAsync());
        }

        // Complete a practice session first on the day:
        // Review XP (+5) + Perfect (+5) + First activity of day (+5) = 15 non-mission XP (< 25!)
        // Completing daily-lesson-1 awards +10 mission bonus XP (total 25 XP), which MUST NOT recursively trigger daily-xp-25!
        using var pStart = await PostJsonAsync(clientA, "/api/v1/learning/practice/start", new StartPracticeRequest());
        pStart.EnsureSuccessStatusCode();
        var pAttempt = await pStart.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        var draft1 = pilotDraft.Lessons.Single(item => item.Id == Lesson1Id);
        foreach (var ex in draft1.Exercises)
        {
            await SubmitDraftExerciseAsync(clientA, pAttempt!.AttemptId, ex, makeWrong: false);
        }

        using var pComplete = await PostJsonAsync(clientA, $"/api/v1/learning/attempts/{pAttempt!.AttemptId}/complete", new { });
        pComplete.EnsureSuccessStatusCode();
        var done = await pComplete.Content.ReadFromJsonAsync<LessonCompletionResponse>();
        Assert.NotNull(done);
        Assert.Contains(done.Rewards.NewlyCompletedMissions, m => m.Code == "daily-lesson-1");
        Assert.DoesNotContain(done.Rewards.NewlyCompletedMissions, m => m.Code == "daily-xp-25");

        var afterMissions = await clientA.GetFromJsonAsync<MissionsOverviewResponse>("/api/v1/gamification/missions");
        var xpMission = afterMissions!.DailyMissions.Single(m => m.Code == "daily-xp-25");
        Assert.False(xpMission.IsCompleted);
        Assert.Equal(15, xpMission.CurrentValue);
    }

    [Fact]
    public async Task Friends_privacy_reciprocal_auto_accept_and_blocks_enforce_server_side_rules()
    {
        using var alice = Client();
        using var bob = Client();
        using var carla = Client();

        var aliceAcc = await RegisterAndGetAccountAsync(alice, "alice_etna");
        var bobAcc = await RegisterAndGetAccountAsync(bob, "bob_aci");
        var carlaAcc = await RegisterAndGetAccountAsync(carla, "carla_ortigia");

        // Self friend request and self block are rejected with 400
        using var selfReq = await PostJsonAsync(alice, "/api/v1/social/friends/requests", new SendFriendRequestPayload(aliceAcc.Id));
        Assert.Equal(HttpStatusCode.BadRequest, selfReq.StatusCode);
        using var selfBlk = await PostJsonAsync(alice, "/api/v1/social/blocks", new BlockUserRequestPayload(aliceAcc.Id));
        Assert.Equal(HttpStatusCode.BadRequest, selfBlk.StatusCode);

        // Carla sets profile visibility to "private" -> hidden from search, profile 403, friend requests 403
        using var carlaPriv = await PostJsonAsync(carla, "/api/v1/social/privacy",
            new UpdatePrivacySettingsRequest("private", AllowFriendRequests: false, ShowInLeaderboard: false));
        carlaPriv.EnsureSuccessStatusCode();

        var searchCarla = await alice.GetFromJsonAsync<UserSearchResponse>("/api/v1/social/users/search?q=carla");
        Assert.Empty(searchCarla!.Users);
        using var viewCarla = await alice.GetAsync("/api/v1/social/profiles/carla_ortigia");
        Assert.Equal(HttpStatusCode.Forbidden, viewCarla.StatusCode);
        using var reqCarla = await PostJsonAsync(alice, "/api/v1/social/friends/requests", new SendFriendRequestPayload(carlaAcc.Id));
        Assert.Equal(HttpStatusCode.Forbidden, reqCarla.StatusCode);

        // Bob sets profile visibility to "friends" -> searchable, but profile 403 until friends!
        using var bobFriendsOnly = await PostJsonAsync(bob, "/api/v1/social/privacy",
            new UpdatePrivacySettingsRequest("friends", AllowFriendRequests: true, ShowInLeaderboard: true));
        bobFriendsOnly.EnsureSuccessStatusCode();

        using var viewBobBefore = await alice.GetAsync("/api/v1/social/profiles/bob_aci");
        Assert.Equal(HttpStatusCode.Forbidden, viewBobBefore.StatusCode);

        // Alice sends friend request to Bob, then Bob sends reciprocal friend request to Alice -> auto-accepted!
        using var aliceToBob = await PostJsonAsync(alice, "/api/v1/social/friends/requests", new SendFriendRequestPayload(bobAcc.Id));
        aliceToBob.EnsureSuccessStatusCode();
        var pendingAction = await aliceToBob.Content.ReadFromJsonAsync<FriendActionResponse>();
        Assert.Equal("outgoing_pending", pendingAction!.RelationshipStatus);
        Assert.False(pendingAction.AutoAccepted);

        using var bobToAlice = await PostJsonAsync(bob, "/api/v1/social/friends/requests", new SendFriendRequestPayload(aliceAcc.Id));
        bobToAlice.EnsureSuccessStatusCode();
        var autoAccepted = await bobToAlice.Content.ReadFromJsonAsync<FriendActionResponse>();
        Assert.Equal("friends", autoAccepted!.RelationshipStatus);
        Assert.True(autoAccepted.AutoAccepted);

        // Now Alice can view Bob's FriendsOnly profile!
        var bobProfile = await alice.GetFromJsonAsync<PublicProfileResponse>("/api/v1/social/profiles/bob_aci");
        Assert.NotNull(bobProfile);
        Assert.Equal("friends", bobProfile.RelationshipStatus);

        // Alice blocks Bob -> friendship is severed, neither sees the other in search, Bob gets 403 on profile & friend request
        using var blockBob = await PostJsonAsync(alice, "/api/v1/social/blocks", new BlockUserRequestPayload(bobAcc.Id));
        Assert.Equal(HttpStatusCode.NoContent, blockBob.StatusCode);

        var aliceOverviewAfterBlock = await alice.GetFromJsonAsync<SocialOverviewResponse>("/api/v1/social/overview");
        Assert.Empty(aliceOverviewAfterBlock!.Friends);
        Assert.Single(aliceOverviewAfterBlock.BlockedUsers);

        var bobSearchAlice = await bob.GetFromJsonAsync<UserSearchResponse>("/api/v1/social/users/search?q=alice");
        Assert.Empty(bobSearchAlice!.Users);
        using var bobReqBlocked = await PostJsonAsync(bob, "/api/v1/social/friends/requests", new SendFriendRequestPayload(aliceAcc.Id));
        Assert.Equal(HttpStatusCode.Forbidden, bobReqBlocked.StatusCode);

        // Unblock restores searchability
        using var unblockBob = await PostJsonAsync(alice, $"/api/v1/social/blocks/{bobAcc.Id}/unblock", new { });
        Assert.Equal(HttpStatusCode.NoContent, unblockBob.StatusCode);
        var bobSearchAfterUnblock = await bob.GetFromJsonAsync<UserSearchResponse>("/api/v1/social/users/search?q=alice");
        Assert.Single(bobSearchAfterUnblock!.Users);
    }

    [Fact]
    public async Task Weekly_leagues_promote_top_ten_with_positive_xp_keep_zero_xp_in_bronze_and_demote_bottom_five_from_silver()
    {
        // Week 1: Monday 2027-04-05
        clock.SetUtcNow(new DateTimeOffset(2027, 4, 5, 10, 0, 0, TimeSpan.Zero));

        using var activeBronzeClient = Client();
        using var zeroXpBronzeClient = Client();
        await RegisterAsync(activeBronzeClient, "bronzo_promosso");
        await RegisterAsync(zeroXpBronzeClient, "bronzo_fermo");

        // bronzo_promosso earns XP in Week 1; bronzo_fermo enrolls by opening leaderboard with 0 XP
        await CompleteLessonPerfectAsync(activeBronzeClient, Lesson1Id);
        var zeroWeek1 = await zeroXpBronzeClient.GetFromJsonAsync<LeaderboardResponse>("/api/v1/social/leaderboard?scope=league");
        Assert.Equal("bronze", zeroWeek1!.CurrentTierCode);

        var activeWeek1 = await activeBronzeClient.GetFromJsonAsync<LeaderboardResponse>("/api/v1/social/leaderboard?scope=league");
        Assert.NotNull(activeWeek1);
        Assert.Equal("bronze", activeWeek1.CurrentTierCode);
        Assert.Equal("promotion", activeWeek1.CurrentUserEntry!.Zone);

        // Create 11 Silver-tier users in Week 1 so we can verify bottom-5 demotion out of Silver at Week 2 rollover:
        // Ranks 1..10 earn positive XP (promote to Gold), Rank 11 earns 0 XP (bottom 5 -> demotes to Bronze!)
        var silverDemotedClient = Client();
        var silverDemotedAcc = await RegisterAndGetAccountAsync(silverDemotedClient, "argento_retrocesso");
        var silverWinnerIds = new List<Guid>();

        for (var i = 1; i <= 10; i++)
        {
            using var c = Client();
            var acc = await RegisterAndGetAccountAsync(c, $"argento_top_{i:D2}");
            silverWinnerIds.Add(acc.Id);
            await CompleteLessonPerfectAsync(c, Lesson1Id);
        }

        // Place these 11 users into Silver for Week 1 (2027-04-05)
        await using (var db = factory.CreateDbContext())
        {
            var week1 = new DateOnly(2027, 4, 5);
            var allSilverIds = silverWinnerIds.Append(silverDemotedAcc.Id).ToList();
            foreach (var uid in allSilverIds)
            {
                var profile = await db.UserLeagueProfiles.SingleOrDefaultAsync(p => p.UserId == uid);
                if (profile is null)
                {
                    db.UserLeagueProfiles.Add(new UserLeagueProfile
                    {
                        UserId = uid,
                        CurrentTier = LeagueTier.Silver,
                        UpdatedAtUtc = clock.GetUtcNow()
                    });
                }
                else
                {
                    profile.CurrentTier = LeagueTier.Silver;
                }

                var standing = await db.WeeklyLeagueStandings
                    .SingleOrDefaultAsync(s => s.WeekStartLocal == week1 && s.UserId == uid);
                if (standing is null)
                {
                    db.WeeklyLeagueStandings.Add(new WeeklyLeagueStanding
                    {
                        WeekStartLocal = week1,
                        UserId = uid,
                        Tier = LeagueTier.Silver,
                        Outcome = LeagueWeekOutcome.InProgress
                    });
                }
                else
                {
                    standing.Tier = LeagueTier.Silver;
                }
            }

            await db.SaveChangesAsync();
        }

        // Advance clock to Week 2: Monday 2027-04-12 08:00 UTC (10:00 CEST Europe/Rome)
        clock.SetUtcNow(new DateTimeOffset(2027, 4, 12, 8, 0, 0, TimeSpan.Zero));

        // 1) bronzo_promosso had XP > 0 in Top 10 -> promoted to Silver!
        var activeWeek2 = await activeBronzeClient.GetFromJsonAsync<LeaderboardResponse>("/api/v1/social/leaderboard?scope=league");
        Assert.NotNull(activeWeek2);
        Assert.Equal("silver", activeWeek2.CurrentTierCode);
        Assert.Equal("promoted", activeWeek2.LastWeekOutcome);

        // 2) bronzo_fermo had 0 XP -> stays in Bronze (neither promoted nor demoted below Bronze)!
        var zeroWeek2 = await zeroXpBronzeClient.GetFromJsonAsync<LeaderboardResponse>("/api/v1/social/leaderboard?scope=league");
        Assert.NotNull(zeroWeek2);
        Assert.Equal("bronze", zeroWeek2.CurrentTierCode);
        Assert.Equal("stayed", zeroWeek2.LastWeekOutcome);

        // 3) argento_retrocesso finished 11th of 11 in Silver with 0 XP (bottom 5) -> demoted to Bronze!
        var demotedWeek2 = await silverDemotedClient.GetFromJsonAsync<LeaderboardResponse>("/api/v1/social/leaderboard?scope=league");
        Assert.NotNull(demotedWeek2);
        Assert.Equal("bronze", demotedWeek2.CurrentTierCode);
        Assert.Equal("demoted", demotedWeek2.LastWeekOutcome);

        silverDemotedClient.Dispose();
    }

    private async Task CompleteLessonPerfectAsync(HttpClient client, Guid lessonId)
    {
        using var start = await PostJsonAsync(client, $"/api/v1/learning/lessons/{lessonId}/attempts", new StartLessonAttemptRequest());
        start.EnsureSuccessStatusCode();
        var attempt = await start.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        var draft = pilotDraft.Lessons.Single(item => item.Id == lessonId);
        foreach (var ex in draft.Exercises)
        {
            await SubmitDraftExerciseAsync(client, attempt!.AttemptId, ex, makeWrong: false);
        }
        using var complete = await PostJsonAsync(client, $"/api/v1/learning/attempts/{attempt!.AttemptId}/complete", new { });
        complete.EnsureSuccessStatusCode();
    }

    private static async Task<ExerciseSubmissionResponse> SubmitDraftExerciseAsync(
        HttpClient client, Guid attemptId, ExerciseDraft draftExercise, bool makeWrong)
    {
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

        using var resp = await PostJsonAsync(
            client,
            $"/api/v1/learning/attempts/{attemptId}/answers",
            new SubmitExerciseAnswerRequest(draftExercise.Id, payload));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<ExerciseSubmissionResponse>();
        return Assert.IsType<ExerciseSubmissionResponse>(body);
    }

    private HttpClient Client() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "register", new RegisterAccountRequest(username, Password, true));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<AccountResponse> RegisterAndGetAccountAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "register", new RegisterAccountRequest(username, Password, true));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RecoveryResponse>();
        return body!.Account;
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

    private static async Task<HttpResponseMessage> PostJsonAsync<T>(HttpClient client, string path, T body)
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
        public void SetUtcNow(DateTimeOffset nextUtc) => currentUtc = nextUtc;
        public void Advance(TimeSpan delta) => currentUtc = currentUtc.Add(delta);
    }
}
