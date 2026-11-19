using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Administration;
using Siculingo.Api.Features.Learning;
using Siculingo.Api.Features.Social;
using Siculingo.Api.Features.System;
using Siculingo.Domain.Administration;
using Siculingo.Infrastructure.Accounts;
using Siculingo.Infrastructure.Editorial;
using Xunit.Abstractions;

namespace Siculingo.IntegrationTests;

public sealed class ReleaseReadinessAndLoadTests : IAsyncLifetime
{
    private const string Password = "frase-sicura-di-test";
    private static readonly Guid Lesson1Id = Guid.Parse("5a100001-0000-4000-8000-000000000001");
    private static readonly Guid Lesson2Id = Guid.Parse("5a100002-0000-4000-8000-000000000002");

    private readonly ITestOutputHelper output;
    private readonly MutableClock clock = new(new DateTimeOffset(2027, 6, 7, 8, 0, 0, TimeSpan.Zero));
    private readonly DatabaseApiFactory factory;
    private PilotDraft pilotDraft = null!;

    public ReleaseReadinessAndLoadTests(ITestOutputHelper output)
    {
        this.output = output;
        factory = new DatabaseApiFactory(clock, new Dictionary<string, string?>
        {
            ["Legal:TermsVersion"] = "2026.06-beta1",
            ["Legal:LastUpdatedIsoDate"] = "2026-06-01",
            ["Legal:ControllerName"] = "Team Siculingo Beta (Test)",
            ["Legal:ContactChannel"] = "Test segnalazioni interne",
            ["Legal:AuditRetentionDays"] = "90"
        });
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

    [Fact]
    public async Task Legal_notice_endpoint_returns_beta_privacy_and_terms_and_registration_requires_explicit_acceptance()
    {
        using var anonymous = Client();
        var legal = await anonymous.GetFromJsonAsync<LegalNoticeResponse>("/api/v1/system/legal");
        Assert.NotNull(legal);
        Assert.Equal("2026.06-beta1", legal.TermsVersion);
        Assert.Equal("2026-06-01", legal.LastUpdatedIsoDate);
        Assert.Equal("Team Siculingo Beta (Test)", legal.ControllerName);
        Assert.Equal(90, legal.AuditRetentionDays);
        Assert.NotEmpty(legal.PrivacySections);
        Assert.NotEmpty(legal.TermsSections);
        Assert.Contains(legal.PrivacySections, s => s.Title.Contains("minimizzazione", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(legal.TermsSections, s => s.Title.Contains("codice di recupero", StringComparison.OrdinalIgnoreCase));

        using var rejected = await PostAuthAsync(anonymous, "register", new RegisterAccountRequest("senza_consenso", Password, AcceptTerms: false));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        await using (var checkDb = factory.CreateDbContext())
        {
            Assert.Equal(0, await checkDb.Users.CountAsync(u => u.UserName == "senza_consenso"));
        }

        using var accepted = await PostAuthAsync(anonymous, "register", new RegisterAccountRequest("con_consenso", Password, AcceptTerms: true));
        accepted.EnsureSuccessStatusCode();

        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.UserName == "con_consenso");
        Assert.Equal("2026.06-beta1", user.AcceptedTermsVersion);
        Assert.Equal(clock.GetUtcNow(), user.AcceptedTermsAtUtc);
    }

    [Fact]
    public async Task Self_service_account_deletion_requires_current_password_wipes_all_personal_data_and_anonymizes_reports_and_audit()
    {
        using var adminClient = Client("198.51.100.10");
        var adminAccount = await RegisterAndGetAccountAsync(adminClient, "admin_privacy");
        await GrantRoleAsync(adminAccount.Id, AdminRoles.Admin);
        await LoginAsync(adminClient, "admin_privacy");

        using var friendClient = Client("198.51.100.11");
        var friendAccount = await RegisterAndGetAccountAsync(friendClient, "amico_privacy");

        using var blockedClient = Client("198.51.100.12");
        var blockedAccount = await RegisterAndGetAccountAsync(blockedClient, "bloccato_privacy");

        using var learnerClient = Client("198.51.100.13");
        var learnerAccount = await RegisterAndGetAccountAsync(learnerClient, "studente_canc");

        // Populate every domain table tied to learnerAccount:
        // 1. Complete Lesson 1 (creates LessonAttempt, ExerciseAnswers, UserLessonCompletion, XpLedgerEntry,
        //    UserStats, UserStreak, UserAchievements, UserHearts, UserDailyMissionProgress, UserLeagueProfile, WeeklyLeagueStanding).
        await CompleteLessonAsync(learnerClient, Lesson1Id, mode: "standard", elapsedSeconds: 210);

        // 2. Update privacy settings so UserPrivacySettings row exists explicitly.
        using (var privacyResp = await PostJsonAsync(
                   learnerClient,
                   "/api/v1/social/privacy",
                   new UpdatePrivacySettingsRequest("friends", true, true)))
        {
            privacyResp.EnsureSuccessStatusCode();
        }

        // 3. Establish mutual friendship with amico_privacy & block bloccato_privacy.
        using (var reqResp = await PostJsonAsync(learnerClient, "/api/v1/social/friends/requests", new SendFriendRequestPayload(friendAccount.Id)))
        {
            reqResp.EnsureSuccessStatusCode();
            var action = await reqResp.Content.ReadFromJsonAsync<FriendActionResponse>();
            using var acceptResp = await PostJsonAsync(friendClient, $"/api/v1/social/friends/requests/{action!.FriendshipId}/accept", new { });
            acceptResp.EnsureSuccessStatusCode();
        }

        using (var blockResp = await PostJsonAsync(learnerClient, "/api/v1/social/blocks", new BlockUserRequestPayload(blockedAccount.Id)))
        {
            blockResp.EnsureSuccessStatusCode();
        }

        // 4. Submit an exercise report that must survive deletion in anonymized form.
        var firstExId = pilotDraft.Lessons.Single(l => l.Id == Lesson1Id).Exercises[0].Id;
        Guid createdReportId;
        using (var reportResp = await PostJsonAsync(
                   learnerClient,
                   "/api/v1/reports/exercises",
                   new CreateExerciseReportRequest(Lesson1Id, firstExId, "variante", "Segnalazione storica da preservare anonimizzata.")))
        {
            reportResp.EnsureSuccessStatusCode();
            var createdReport = await reportResp.Content.ReadFromJsonAsync<ReportCreatedResponse>();
            createdReportId = createdReport!.ReportId;
        }

        // 5. Grant Editor temporarily so learner creates an AdminAuditEntry row (ActorUserId == learnerAccount.Id).
        await GrantRoleAsync(learnerAccount.Id, AdminRoles.Editor);
        await LoginAsync(learnerClient, "studente_canc");
        Guid createdLexemeId;
        using (var lexemeResp = await PostJsonAsync(
                   learnerClient,
                   "/api/v1/admin/lexemes",
                   new UpsertLinguisticLexemeRequest(
                       null,
                       "manciaficodi_test",
                       "espressione di prova redazionale",
                       "contesto colloquiale di test",
                       "colloquiale",
                       null,
                       "Nota interna test",
                       false)))
        {
            lexemeResp.EnsureSuccessStatusCode();
            var createdLexeme = await lexemeResp.Content.ReadFromJsonAsync<LinguisticLexemeDto>();
            createdLexemeId = createdLexeme!.Id;
        }

        // Wrong password must be rejected without deleting anything.
        using (var wrongPwd = await PostAuthAsync(learnerClient, "delete", new DeleteAccountRequest("password-sbagliata-123")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrongPwd.StatusCode);
        }

        var stillAuthenticated = await learnerClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillAuthenticated.StatusCode);

        // Correct password deletes the account atomically and signs out immediately.
        using (var deleteResp = await PostAuthAsync(learnerClient, "delete", new DeleteAccountRequest(Password)))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
        }

        var afterDeleteMe = await learnerClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDeleteMe.StatusCode);

        await using (var db = factory.CreateDbContext())
        {
            var id = learnerAccount.Id;
            Assert.False(await db.Users.AnyAsync(u => u.Id == id));
            Assert.False(await db.AccountRecoveryCodes.AnyAsync(r => r.UserId == id));
            Assert.False(await db.LessonAttempts.AnyAsync(a => a.UserId == id));
            Assert.False(await db.UserLessonCompletions.AnyAsync(c => c.UserId == id));
            Assert.False(await db.XpLedger.AnyAsync(x => x.UserId == id));
            Assert.False(await db.UserStats.AnyAsync(s => s.UserId == id));
            Assert.False(await db.UserStreaks.AnyAsync(s => s.UserId == id));
            Assert.False(await db.UserAchievements.AnyAsync(a => a.UserId == id));
            Assert.False(await db.UserHearts.AnyAsync(h => h.UserId == id));
            Assert.False(await db.UserPrivacySettings.AnyAsync(p => p.UserId == id));
            Assert.False(await db.UserMissionProgress.AnyAsync(m => m.UserId == id));
            Assert.False(await db.Friendships.AnyAsync(f => f.UserLowId == id || f.UserHighId == id));
            Assert.False(await db.UserBlocks.AnyAsync(b => b.BlockerUserId == id || b.BlockedUserId == id));
            Assert.False(await db.UserLeagueProfiles.AnyAsync(l => l.UserId == id));
            Assert.False(await db.WeeklyLeagueStandings.AnyAsync(w => w.UserId == id));

            var preservedReport = await db.UserReports.SingleAsync(r => r.Id == createdReportId);
            Assert.Null(preservedReport.ReporterUserId);

            var preservedAudit = await db.AdminAuditEntries.SingleAsync(a => a.TargetIdentifier == createdLexemeId.ToString());
            Assert.Null(preservedAudit.ActorUserId);
            Assert.Equal("[account-eliminato]", preservedAudit.ActorUsername);
        }

        // Verify Admin endpoints display anonymized snapshots cleanly without 500s.
        var reports = await adminClient.GetFromJsonAsync<AdminReportListResponse>("/api/v1/admin/reports");
        var reportDto = Assert.Single(reports!.Items, r => r.Id == createdReportId);
        Assert.Null(reportDto.ReporterUserId);
        Assert.Equal("[account eliminato]", reportDto.ReporterUsername);

        var audit = await adminClient.GetFromJsonAsync<AdminAuditListResponse>("/api/v1/admin/audit");
        var auditDto = Assert.Single(audit!.Items, a => a.TargetIdentifier == createdLexemeId.ToString());
        Assert.Null(auditDto.ActorUserId);
        Assert.Equal("[account-eliminato]", auditDto.ActorUsername);

        // Deleted username is completely free to be registered again.
        using var recycledClient = Client("198.51.100.14");
        var recycled = await RegisterAndGetAccountAsync(recycledClient, "studente_canc");
        Assert.NotEqual(learnerAccount.Id, recycled.Id);
    }

    [Fact]
    public async Task Admin_product_metrics_computes_pseudonymous_kpis_d1_d7_cohorts_and_exercise_hotspots()
    {
        using var studentAClient = Client("198.51.100.21");
        var studentA = await RegisterAndGetAccountAsync(studentAClient, "metriche_a");

        // Non-admin cannot access product metrics.
        using (var forbidden = await studentAClient.GetAsync("/api/v1/admin/product-metrics"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using var studentBClient = Client("198.51.100.22");
        var studentB = await RegisterAndGetAccountAsync(studentBClient, "metriche_b");

        using var adminClient = Client("198.51.100.20");
        var admin = await RegisterAndGetAccountAsync(adminClient, "admin_metriche");
        await GrantRoleAsync(admin.Id, AdminRoles.Admin);
        await LoginAsync(adminClient, "admin_metriche");

        // Day 0 (2026-06-01):
        // - studentA completes Lesson 1 in 240s (inside 180-300s target), making 1 deliberate mistake on exercise 0,
        //   files an open report on exercise 0, and becomes friends with studentB.
        await CompleteLessonAsync(studentAClient, Lesson1Id, mode: "standard", elapsedSeconds: 240, wrongFirstAttemptOnExerciseZero: true);

        var lesson1Ex0 = pilotDraft.Lessons.Single(l => l.Id == Lesson1Id).Exercises[0].Id;
        using (var reportResp = await PostJsonAsync(
                   studentAClient,
                   "/api/v1/reports/exercises",
                   new CreateExerciseReportRequest(Lesson1Id, lesson1Ex0, "traduzione", "Dubbio su variante lessicale.")))
        {
            reportResp.EnsureSuccessStatusCode();
        }

        using (var friendReq = await PostJsonAsync(studentAClient, "/api/v1/social/friends/requests", new SendFriendRequestPayload(studentB.Id)))
        {
            friendReq.EnsureSuccessStatusCode();
            var action = await friendReq.Content.ReadFromJsonAsync<FriendActionResponse>();
            using var acceptResp = await PostJsonAsync(studentBClient, $"/api/v1/social/friends/requests/{action!.FriendshipId}/accept", new { });
            acceptResp.EnsureSuccessStatusCode();
        }

        // - studentB completes Lesson 1 in 420s (outside 180-300s target) and then a practice attempt in 200s (inside target).
        await CompleteLessonAsync(studentBClient, Lesson1Id, mode: "standard", elapsedSeconds: 420);
        await CompleteLessonAsync(studentBClient, Lesson1Id, mode: "practice", elapsedSeconds: 200);

        // Day 1 (2026-06-02): studentA returns and completes Lesson 2 in 210s (inside target). studentB does NOT return on D1.
        clock.Advance(TimeSpan.FromDays(1));
        await CompleteLessonAsync(studentAClient, Lesson2Id, mode: "standard", elapsedSeconds: 210);

        // Day 7 (2026-06-08): studentA returns and completes a practice attempt on Lesson 1 in 195s (inside target).
        clock.Advance(TimeSpan.FromDays(6));
        await CompleteLessonAsync(studentAClient, Lesson1Id, mode: "practice", elapsedSeconds: 195);

        // Advance to Day 8 (2026-06-09) so Day 0 registrations (studentA, studentB, admin) are eligible for both D1 and D7.
        clock.Advance(TimeSpan.FromDays(1));

        var metrics = await adminClient.GetFromJsonAsync<ProductMetricsResponse>("/api/v1/admin/product-metrics?windowDays=30");
        Assert.NotNull(metrics);
        Assert.Equal(30, metrics.WindowDays);
        Assert.Equal(3, metrics.TotalRegisteredUsers);
        Assert.Equal(2, metrics.ActiveLearnersDenominator);
        Assert.Equal(5, metrics.LessonsStartedInWindow);
        Assert.Equal(5, metrics.LessonsCompletedInWindow);
        Assert.Equal(100.0, metrics.LessonCompletionRatePercent);
        Assert.Equal(2.5, metrics.AverageCompletedLessonsPerActiveLearner);

        // Durations: [195, 200, 210, 240, 420] -> median = 210s, 4 of 5 (80%) inside [180, 300]s!
        Assert.Equal(5, metrics.DurationMeasuredAttemptsCount);
        Assert.Equal(210.0, metrics.MedianLessonDurationSeconds);
        Assert.Equal(80.0, metrics.ShareWithinThreeToFiveMinutesPercent);

        // Practice adoption: both studentA and studentB completed 1 practice session -> 2 completions, 2 of 2 learners (100%).
        Assert.Equal(2, metrics.PracticeCompletionsInWindow);
        Assert.Equal(2, metrics.LearnersUsingPracticeInWindow);
        Assert.Equal(100.0, metrics.PracticeAdoptionRatePercent);

        // D1 & D7 cohorts: 3 users registered on Day 0, only studentA returned on D1 and D7.
        Assert.Equal(3, metrics.D1EligibleCohortCount);
        Assert.Equal(1, metrics.D1ReturnedCount);
        Assert.Equal(33.3, metrics.D1ReturnRatePercent);
        Assert.Equal(3, metrics.D7EligibleCohortCount);
        Assert.Equal(1, metrics.D7ReturnedCount);
        Assert.Equal(33.3, metrics.D7ReturnRatePercent);

        // Friends: 2 of 3 registered users have >= 1 accepted friend (66.7%).
        Assert.Equal(2, metrics.UsersWithAcceptedFriendsCount);
        Assert.Equal(66.7, metrics.FriendAdoptionRatePercent);

        // Hotspots: Lesson 1 Exercise 0 has 1 open report and 1 wrong answer out of 5 completed attempts on Lesson 1.
        var hotspot = Assert.Single(metrics.ExerciseAccuracyItems, h => h.LessonId == Lesson1Id && h.ExerciseId == lesson1Ex0);
        Assert.Equal(1, hotspot.OpenReportsCount);
        Assert.True(hotspot.AccuracyPercent < 100.0);
    }

    [Fact]
    public async Task Cold_start_and_representative_25_active_users_plus_100_concurrent_readers_succeed_within_latency_budgets()
    {
        var coldClock = new MutableClock(new DateTimeOffset(2027, 6, 14, 9, 0, 0, TimeSpan.Zero));
        await using var coldFactory = new DatabaseApiFactory(coldClock);
        try
        {
            var approvedJson = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "pilot-greetings.v1.json"));

            // 1. Cold-start database migration + seed + first HTTP readiness & legal & path requests
            await using (var setupDb = coldFactory.CreateDbContext())
            {
                await setupDb.Database.MigrateAsync();
                await PilotDraftImporter.PublishApprovedAsync(setupDb, approvedJson);
            }

            var coldHostSw = Stopwatch.StartNew();
            using var probeClient = CreateFactoryClient(coldFactory, "198.51.100.250");
            using var coldReady = await probeClient.GetAsync("/health/ready");
            coldHostSw.Stop();
            Assert.Equal(HttpStatusCode.OK, coldReady.StatusCode);
            var coldReadyMs = coldHostSw.Elapsed.TotalMilliseconds;

            var coldRegisterSw = Stopwatch.StartNew();
            await RegisterOnClientAsync(probeClient, "cold_probe");
            coldRegisterSw.Stop();
            var coldRegisterMs = coldRegisterSw.Elapsed.TotalMilliseconds;

            var coldPathSw = Stopwatch.StartNew();
            using var coldPathResp = await probeClient.GetAsync("/api/v1/learning/path");
            coldPathSw.Stop();
            Assert.Equal(HttpStatusCode.OK, coldPathResp.StatusCode);
            var coldPathMs = coldPathSw.Elapsed.TotalMilliseconds;

            // Warm remaining JIT / EF query caches once via probeClient after recording cold-start metrics
            var lesson1Exercises = pilotDraft.Lessons.Single(l => l.Id == Lesson1Id).Exercises;
            using (await probeClient.GetAsync("/api/v1/gamification/hearts")) { }
            using (await probeClient.GetAsync("/api/v1/gamification/missions")) { }
            using (var warmStartResp = await PostJsonAsync(
                       probeClient,
                       $"/api/v1/learning/lessons/{Lesson1Id}/attempts",
                       new StartLessonAttemptRequest(ReplaceActive: false, PracticeMode: false)))
            {
                warmStartResp.EnsureSuccessStatusCode();
                var warmAttempt = await warmStartResp.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
                foreach (var ex in lesson1Exercises)
                {
                    await SubmitDraftExerciseAsync(probeClient, warmAttempt!.AttemptId, ex, makeWrong: false);
                }

                using var warmCompleteResp = await PostJsonAsync(
                    probeClient,
                    $"/api/v1/learning/attempts/{warmAttempt!.AttemptId}/complete",
                    new { });
                warmCompleteResp.EnsureSuccessStatusCode();
            }

            using (await probeClient.GetAsync("/api/v1/social/leaderboard?scope=league")) { }

            // 2. Provision 25 distinct active beta users (each with its own cookie jar and client IP)
            const int activeUsersCount = 25;
            var userClients = new List<HttpClient>(activeUsersCount);
            var betaUserIds = new HashSet<Guid>(activeUsersCount);
            for (var i = 1; i <= activeUsersCount; i++)
            {
                var client = CreateFactoryClient(coldFactory, $"198.51.101.{i}");
                var registered = await RegisterAndGetAccountAsync(client, $"beta_user_{i:00}");
                betaUserIds.Add(registered.Id);
                userClients.Add(client);
            }

            try
            {
                // 3. Representative load: 25 concurrent active users executing full study flows simultaneously on SQL Server
                var warmEndpointLatenciesMs = new ConcurrentBag<double>();

                var activeFlowSw = Stopwatch.StartNew();
                await Task.WhenAll(userClients.Select(async client =>
                {
                    await MeasureGetAsync(client, "/api/v1/learning/path", warmEndpointLatenciesMs);
                    await MeasureGetAsync(client, "/api/v1/gamification/hearts", warmEndpointLatenciesMs);
                    await MeasureGetAsync(client, "/api/v1/gamification/missions", warmEndpointLatenciesMs);

                    var startSw = Stopwatch.StartNew();
                    using var startResp = await PostJsonAsync(
                        client,
                        $"/api/v1/learning/lessons/{Lesson1Id}/attempts",
                        new StartLessonAttemptRequest(ReplaceActive: false, PracticeMode: false));
                    startSw.Stop();
                    Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
                    warmEndpointLatenciesMs.Add(startSw.Elapsed.TotalMilliseconds);

                    var attempt = await startResp.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
                    Assert.NotNull(attempt);

                    foreach (var ex in lesson1Exercises)
                    {
                        var answerSw = Stopwatch.StartNew();
                        await SubmitDraftExerciseAsync(client, attempt.AttemptId, ex, makeWrong: false);
                        answerSw.Stop();
                        warmEndpointLatenciesMs.Add(answerSw.Elapsed.TotalMilliseconds);
                    }

                    var completeSw = Stopwatch.StartNew();
                    using var completeResp = await PostJsonAsync(
                        client,
                        $"/api/v1/learning/attempts/{attempt.AttemptId}/complete",
                        new { });
                    completeSw.Stop();
                    Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
                    warmEndpointLatenciesMs.Add(completeSw.Elapsed.TotalMilliseconds);

                    await MeasureGetAsync(client, "/api/v1/social/leaderboard?scope=league", warmEndpointLatenciesMs);
                }));
                activeFlowSw.Stop();

                // Verify zero data corruption / lost updates across 25 concurrent lesson completions on SQL Server:
                // 20 base XP + 5 perfect bonus + 15 daily lesson mission + 16 daily 25-XP mission = 56 XP per user.
                await using (var verifyDb = coldFactory.CreateDbContext())
                {
                    var statsList = await verifyDb.UserStats.AsNoTracking()
                        .Where(s => betaUserIds.Contains(s.UserId))
                        .ToListAsync();
                    Assert.Equal(activeUsersCount, statsList.Count);
                    Assert.All(statsList, stat => Assert.Equal(56, stat.TotalXp));

                    var completedCount = await verifyDb.UserLessonCompletions
                        .CountAsync(c => betaUserIds.Contains(c.UserId));
                    Assert.Equal(activeUsersCount, completedCount);
                }

                // 4. Exploratory burst load: 100 concurrent read requests across core endpoints
                const int concurrentReaders = 100;
                var burstReadLatenciesMs = new ConcurrentBag<double>();
                string[] burstEndpoints =
                [
                    "/api/v1/learning/path",
                    "/api/v1/social/leaderboard?scope=league",
                    "/api/v1/system/legal",
                    "/health/ready"
                ];

                var burstSw = Stopwatch.StartNew();
                await Task.WhenAll(Enumerable.Range(0, concurrentReaders).Select(async idx =>
                {
                    var client = userClients[idx % userClients.Count];
                    var endpoint = burstEndpoints[idx % burstEndpoints.Length];
                    await MeasureGetAsync(client, endpoint, burstReadLatenciesMs);
                }));
                burstSw.Stop();

                var warmStats = ComputePercentiles(warmEndpointLatenciesMs);
                var burstStats = ComputePercentiles(burstReadLatenciesMs);

                output.WriteLine(
                    $"[COLD START] /health/ready={coldReadyMs:F1}ms | /auth/register={coldRegisterMs:F1}ms | /learning/path={coldPathMs:F1}ms");
                output.WriteLine(
                    $"[25 ACTIVE USERS CONCURRENT] totalWall={activeFlowSw.Elapsed.TotalMilliseconds:F1}ms | requests={warmStats.Count} | p50={warmStats.P50:F1}ms | p95={warmStats.P95:F1}ms | max={warmStats.Max:F1}ms");
                output.WriteLine(
                    $"[100 CONCURRENT READERS] totalWall={burstSw.Elapsed.TotalMilliseconds:F1}ms | requests={burstStats.Count} | p50={burstStats.P50:F1}ms | p95={burstStats.P95:F1}ms | max={burstStats.Max:F1}ms");

                Assert.Equal(concurrentReaders, burstStats.Count);
                Assert.True(warmStats.P95 < 2500, $"Expected 25-user warm p95 < 2500ms, actual: {warmStats.P95:F1}ms");
                Assert.True(burstStats.P95 < 2500, $"Expected 100-reader burst p95 < 2500ms, actual: {burstStats.P95:F1}ms");
            }
            finally
            {
                foreach (var c in userClients)
                    c.Dispose();
            }
        }
        finally
        {
            await coldFactory.DeleteTestDatabaseAsync();
        }
    }

    private static async Task MeasureGetAsync(HttpClient client, string url, ConcurrentBag<double> sink)
    {
        var sw = Stopwatch.StartNew();
        using var response = await client.GetAsync(url);
        sw.Stop();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        sink.Add(sw.Elapsed.TotalMilliseconds);
    }

    private static (int Count, double P50, double P95, double Max) ComputePercentiles(IEnumerable<double> samples)
    {
        var sorted = samples.OrderBy(x => x).ToArray();
        if (sorted.Length == 0)
            return (0, 0, 0, 0);

        double Percentile(double p)
        {
            var index = (int)Math.Ceiling(p * sorted.Length) - 1;
            index = Math.Clamp(index, 0, sorted.Length - 1);
            return sorted[index];
        }

        return (sorted.Length, Percentile(0.50), Percentile(0.95), sorted[^1]);
    }

    private async Task CompleteLessonAsync(
        HttpClient client,
        Guid lessonId,
        string mode,
        int elapsedSeconds,
        bool wrongFirstAttemptOnExerciseZero = false)
    {
        var isPractice = string.Equals(mode, "practice", StringComparison.OrdinalIgnoreCase);
        using var start = await PostJsonAsync(
            client,
            $"/api/v1/learning/lessons/{lessonId}/attempts",
            new StartLessonAttemptRequest(ReplaceActive: false, PracticeMode: isPractice));
        start.EnsureSuccessStatusCode();
        var attempt = await start.Content.ReadFromJsonAsync<LessonAttemptResponse>(PilotDraft.JsonOptions);
        var draft = pilotDraft.Lessons.Single(item => item.Id == lessonId);

        for (var i = 0; i < draft.Exercises.Count; i++)
        {
            var ex = draft.Exercises[i];
            if (i == 0 && wrongFirstAttemptOnExerciseZero)
            {
                await SubmitDraftExerciseAsync(client, attempt!.AttemptId, ex, makeWrong: true);
            }
            await SubmitDraftExerciseAsync(client, attempt!.AttemptId, ex, makeWrong: false);
        }

        clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));
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

    private async Task GrantRoleAsync(Guid userId, string roleName)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            var result = await userManager.AddToRoleAsync(user, roleName);
            Assert.True(result.Succeeded);
        }
    }

    private HttpClient Client(string? remoteIp = null) => CreateFactoryClient(factory, remoteIp);

    private static HttpClient CreateFactoryClient(DatabaseApiFactory targetFactory, string? remoteIp)
    {
        var client = targetFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            client.DefaultRequestHeaders.Add("X-Test-Remote-Ip", remoteIp);
        }
        return client;
    }

    private static async Task RegisterOnClientAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "register", new RegisterAccountRequest(username, Password, AcceptTerms: true));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<AccountResponse> RegisterAndGetAccountAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "register", new RegisterAccountRequest(username, Password, AcceptTerms: true));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RecoveryResponse>();
        return body!.Account;
    }

    private static async Task LoginAsync(HttpClient client, string username)
    {
        using var response = await PostAuthAsync(client, "login", new CredentialsRequest(username, Password));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> PostAuthAsync<T>(HttpClient client, string path, T body) =>
        await PostJsonAsync(client, $"/api/v1/auth/{path}", body);

    private static async Task<HttpResponseMessage> PostJsonAsync<T>(HttpClient client, string path, T body)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        return await client.SendAsync(request);
    }

    public async Task DisposeAsync()
    {
        await factory.DeleteTestDatabaseAsync();
        await factory.DisposeAsync();
    }

    private sealed class MutableClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset now = initial;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now = now.Add(delta);
    }
}
