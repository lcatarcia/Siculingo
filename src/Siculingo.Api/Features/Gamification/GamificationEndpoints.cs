using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siculingo.Api.Common;
using Siculingo.Domain.Gamification;
using Siculingo.Domain.Learning;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Gamification;

public static class GamificationEndpoints
{
    public static void MapGamification(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/gamification")
            .WithTags("Gamification")
            .RequireAuthorization();

        group.MapGet("/summary", GetSummary)
            .WithName("GetGamificationSummary")
            .ProducesProblem(401);

        group.MapGet("/achievements", GetAchievements)
            .WithName("GetUserAchievements")
            .ProducesProblem(401);

        group.MapGet("/hearts", GetHearts)
            .WithName("GetUserHearts")
            .ProducesProblem(401);

        group.MapGet("/missions", GetMissions)
            .WithName("GetUserMissions")
            .ProducesProblem(401);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static async Task<Results<Ok<GamificationSummaryResponse>, UnauthorizedHttpResult>> GetSummary(
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        IOptionsSnapshot<HeartsOptions> heartsOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var nowUtc = clock.GetUtcNow();
        var stats = await db.UserStats.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var streak = await db.UserStreaks.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var thresholds = await db.LevelThresholds.AsNoTracking()
            .OrderBy(item => item.Level)
            .ToListAsync(cancellationToken);

        var timeZone = GamificationCalculator.ResolveTimeZone(streak?.TimeZoneId);
        var todayLocal = GamificationCalculator.ToLocalDate(nowUtc, timeZone);
        var weekStart = GamificationCalculator.GetWeekStartMonday(todayLocal);
        var weekEnd = weekStart.AddDays(6);

        var ledgerTotalXp = await db.XpLedger.AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .SumAsync(entry => (int?)entry.Amount, cancellationToken) ?? 0;

        var todayXp = await db.XpLedger.AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.LocalActivityDate == todayLocal)
            .SumAsync(entry => (int?)entry.Amount, cancellationToken) ?? 0;

        var weeklyXp = await db.XpLedger.AsNoTracking()
            .Where(entry => entry.UserId == userId &&
                            entry.LocalActivityDate >= weekStart &&
                            entry.LocalActivityDate <= weekEnd)
            .SumAsync(entry => (int?)entry.Amount, cancellationToken) ?? 0;

        var rewardedReviewsToday = await db.XpLedger.AsNoTracking()
            .CountAsync(
                entry => entry.UserId == userId &&
                         entry.LocalActivityDate == todayLocal &&
                         entry.EventType == XpEventType.ReviewCompletion,
                cancellationToken);

        var unlockedCount = await db.UserAchievements.AsNoTracking()
            .CountAsync(item => item.UserId == userId, cancellationToken);
        var totalAchievementsCount = await db.Achievements.AsNoTracking()
            .CountAsync(item => item.IsActive, cancellationToken);

        var levelProgress = GamificationCalculator.CalculateLevelProgress(ledgerTotalXp, thresholds);
        var effectiveCurrentStreak = GamificationCalculator.CalculateEffectiveCurrentStreak(streak, todayLocal);
        var studiedToday = streak?.LastActivityDateLocal == todayLocal;

        var recentXpEvents = await db.XpLedger.AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.EventType)
            .Take(10)
            .Select(entry => new RecentXpLedgerItemDto(
                entry.Id,
                GamificationCalculator.FormatEventCode(entry.EventType),
                GamificationCalculator.DescribeXpEventType(entry.EventType),
                entry.Amount,
                entry.LocalActivityDate.ToString("yyyy-MM-dd"),
                entry.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var hearts = await HeartsService.GetStatusSnapshotAsync(
            db,
            userId,
            nowUtc,
            heartsOptions.Value.ConsumptionEnabled,
            cancellationToken);

        return TypedResults.Ok(new GamificationSummaryResponse(
            levelProgress.TotalXp,
            (stats?.TotalXp ?? 0) == ledgerTotalXp,
            todayXp,
            weeklyXp,
            rewardedReviewsToday,
            GamificationDefaults.DailyRewardedReviewCap,
            levelProgress.CurrentLevel,
            levelProgress.LevelTitle,
            levelProgress.CurrentLevelMinXp,
            levelProgress.NextLevelMinXp,
            levelProgress.XpIntoLevel,
            levelProgress.XpNeededForNextLevel,
            levelProgress.ProgressPercent,
            effectiveCurrentStreak,
            streak?.BestStreak ?? 0,
            studiedToday,
            streak?.LastActivityDateLocal?.ToString("yyyy-MM-dd"),
            streak?.TimeZoneId ?? GamificationDefaults.DefaultTimeZoneId,
            stats?.UniqueLessonsCompleted ?? 0,
            stats?.TotalLessonCompletions ?? 0,
            stats?.PerfectLessonsCount ?? 0,
            stats?.ConsecutiveCorrectAnswers ?? 0,
            stats?.BestConsecutiveCorrectAnswers ?? 0,
            unlockedCount,
            totalAchievementsCount,
            recentXpEvents,
            hearts));
    }

    private static async Task<Results<Ok<AchievementsCatalogResponse>, UnauthorizedHttpResult>> GetAchievements(
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var nowUtc = clock.GetUtcNow();
        var stats = await db.UserStats.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var streak = await db.UserStreaks.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        var timeZone = GamificationCalculator.ResolveTimeZone(streak?.TimeZoneId);
        var todayLocal = GamificationCalculator.ToLocalDate(nowUtc, timeZone);
        var effectiveStreak = GamificationCalculator.CalculateEffectiveCurrentStreak(streak, todayLocal);

        var definitions = await db.Achievements.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);

        var unlockedByAchievementId = await db.UserAchievements.AsNoTracking()
            .Where(item => item.UserId == userId)
            .ToDictionaryAsync(item => item.AchievementId, item => item.UnlockedAtUtc, cancellationToken);

        var completedLessonIds = (await db.UserLessonCompletions.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => item.LessonId)
            .ToListAsync(cancellationToken)).ToHashSet();

        // Compute max completed lessons within any single published unit for exploration progress bar
        var bestUnitProgress = await CalculateBestPublishedUnitProgressAsync(db, completedLessonIds, cancellationToken);

        var items = definitions.Select(def =>
        {
            var isUnlocked = unlockedByAchievementId.TryGetValue(def.Id, out var unlockedAt);
            var (currentValue, targetValue) = def.Code switch
            {
                GamificationDefaults.AchievementCodes.FirstLesson =>
                    (Math.Min(stats?.TotalLessonCompletions ?? 0, def.Threshold), def.Threshold),
                GamificationDefaults.AchievementCodes.PerfectLesson =>
                    (Math.Min(stats?.PerfectLessonsCount ?? 0, def.Threshold), def.Threshold),
                GamificationDefaults.AchievementCodes.PrecisionTen =>
                    (Math.Min(stats?.BestConsecutiveCorrectAnswers ?? 0, def.Threshold), def.Threshold),
                GamificationDefaults.AchievementCodes.StreakThree =>
                    (Math.Min(Math.Max(effectiveStreak, streak?.BestStreak ?? 0), def.Threshold), def.Threshold),
                GamificationDefaults.AchievementCodes.StreakSeven =>
                    (Math.Min(Math.Max(effectiveStreak, streak?.BestStreak ?? 0), def.Threshold), def.Threshold),
                GamificationDefaults.AchievementCodes.PilotUnitComplete =>
                    (isUnlocked ? bestUnitProgress.Target : bestUnitProgress.Current, bestUnitProgress.Target),
                _ => (isUnlocked ? def.Threshold : 0, def.Threshold)
            };

            var progressPercent = targetValue <= 0
                ? 100
                : Math.Clamp((int)Math.Round(currentValue * 100.0 / targetValue, MidpointRounding.AwayFromZero), 0, 100);

            return new AchievementStatusItemDto(
                def.Code,
                def.Name,
                def.Description,
                GamificationCalculator.FormatCategory(def.Category),
                GamificationCalculator.FormatRarity(def.Rarity),
                def.IconPath,
                isUnlocked,
                isUnlocked ? unlockedAt : null,
                isUnlocked ? targetValue : currentValue,
                targetValue,
                isUnlocked ? 100 : progressPercent);
        }).ToList();

        return TypedResults.Ok(new AchievementsCatalogResponse(
            items.Count(item => item.IsUnlocked),
            items.Count,
            items));
    }

    private static async Task<(int Current, int Target)> CalculateBestPublishedUnitProgressAsync(
        SiculingoDbContext db,
        IReadOnlySet<Guid> completedLessonIds,
        CancellationToken cancellationToken)
    {
        var publishedUnitLessons = await (
            from course in db.Courses.AsNoTracking()
            where course.IsPublished
            from section in course.Sections
            where section.IsPublished
            from unit in section.Units
            where unit.IsPublished
            from lesson in unit.Lessons
            where lesson.IsPublished && lesson.PublishedSnapshotJson != null
            select new { UnitId = unit.Id, lesson.Id })
            .ToListAsync(cancellationToken);

        var bestCurrent = 0;
        var bestTarget = 5;
        var bestRatio = -1.0;

        foreach (var unitGroup in publishedUnitLessons.GroupBy(item => item.UnitId))
        {
            var lessonIds = unitGroup.Select(item => item.Id).ToList();
            var done = lessonIds.Count(completedLessonIds.Contains);
            var ratio = (double)done / lessonIds.Count;
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                bestCurrent = done;
                bestTarget = lessonIds.Count;
            }
        }

        return (bestCurrent, bestTarget);
    }

    private static async Task<Results<Ok<HeartsStatusDto>, UnauthorizedHttpResult>> GetHearts(
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        IOptionsSnapshot<HeartsOptions> heartsOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var snapshot = await HeartsService.GetStatusSnapshotAsync(
            db,
            userId,
            clock.GetUtcNow(),
            heartsOptions.Value.ConsumptionEnabled,
            cancellationToken);

        return TypedResults.Ok(snapshot);
    }

    private static async Task<Results<Ok<MissionsOverviewResponse>, UnauthorizedHttpResult>> GetMissions(
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var nowUtc = clock.GetUtcNow();
        var streak = await db.UserStreaks.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var timeZone = GamificationCalculator.ResolveTimeZone(streak?.TimeZoneId);
        var todayLocal = GamificationCalculator.ToLocalDate(nowUtc, timeZone);
        var weekStartLocal = GamificationCalculator.GetWeekStartMonday(todayLocal);
        var weekEndLocal = weekStartLocal.AddDays(6);

        // Ensure current-period mission rows are assigned without duplicates
        for (var retry = 0; retry < 3; retry++)
        {
            var existingCount = await db.UserMissionProgress.AsNoTracking()
                .CountAsync(
                    item => item.UserId == userId &&
                            ((item.Scope == MissionScope.Daily && item.PeriodStartLocal == todayLocal) ||
                             (item.Scope == MissionScope.Weekly && item.PeriodStartLocal == weekStartLocal)),
                    cancellationToken);

            if (existingCount >= GamificationDefaults.MissionCatalog.Count)
                break;

            try
            {
                await using var tx = await SqlConcurrencyLock.BeginUserTransactionAsync(db, userId, cancellationToken);
                var existing = await db.UserMissionProgress
                    .Where(item => item.UserId == userId &&
                                   (item.PeriodStartLocal == todayLocal || item.PeriodStartLocal == weekStartLocal))
                    .ToListAsync(cancellationToken);

                var windowStartUtc = new DateTimeOffset(weekStartLocal.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-2);
                var completedAttempts = await db.LessonAttempts.AsNoTracking()
                    .Where(item => item.UserId == userId &&
                                   item.Status == LessonAttemptStatus.Completed &&
                                   item.CompletedAtUtc != null &&
                                   item.CompletedAtUtc >= windowStartUtc)
                    .Select(item => new
                    {
                        CompletedAtUtc = item.CompletedAtUtc!.Value,
                        item.ExerciseCount,
                        CorrectCount = item.Answers.Count(answer => answer.IsCorrect),
                        TotalAnswers = item.Answers.Count
                    })
                    .ToListAsync(cancellationToken);

                var completionsToday = 0;
                var correctToday = 0;
                var completionsWeek = 0;
                var perfectWeek = 0;

                foreach (var att in completedAttempts)
                {
                    var localDate = GamificationCalculator.ToLocalDate(att.CompletedAtUtc, timeZone);
                    if (localDate < weekStartLocal || localDate > weekEndLocal)
                        continue;

                    completionsWeek += 1;
                    if (att.ExerciseCount > 0 && att.TotalAnswers == att.ExerciseCount && att.CorrectCount == att.ExerciseCount)
                        perfectWeek += 1;
                    if (localDate == todayLocal)
                    {
                        completionsToday += 1;
                        correctToday += att.CorrectCount;
                    }
                }

                var lessonXpToday = await db.XpLedger.AsNoTracking()
                    .Where(entry => entry.UserId == userId &&
                                    entry.LocalActivityDate == todayLocal &&
                                    entry.EventType != XpEventType.DailyMissionBonus &&
                                    entry.EventType != XpEventType.WeeklyMissionBonus)
                    .SumAsync(entry => (int?)entry.Amount, cancellationToken) ?? 0;

                foreach (var template in GamificationDefaults.MissionCatalog)
                {
                    var periodStart = template.Scope == MissionScope.Daily ? todayLocal : weekStartLocal;
                    if (existing.Any(item => item.MissionCode == template.Code && item.PeriodStartLocal == periodStart))
                        continue;

                    var current = template.Code switch
                    {
                        GamificationDefaults.MissionCodes.DailyLessonOne => completionsToday,
                        GamificationDefaults.MissionCodes.DailyXpTwentyFive => lessonXpToday,
                        GamificationDefaults.MissionCodes.DailyCorrectTen => correctToday,
                        GamificationDefaults.MissionCodes.WeeklyLessonsFour => completionsWeek,
                        GamificationDefaults.MissionCodes.WeeklyPerfectTwo => perfectWeek,
                        _ => 0
                    };

                    db.UserMissionProgress.Add(new UserMissionProgress
                    {
                        UserId = userId,
                        Scope = template.Scope,
                        MissionCode = template.Code,
                        PeriodStartLocal = periodStart,
                        CurrentValue = Math.Min(current, template.TargetValue),
                        TargetValue = template.TargetValue,
                        RewardXp = template.RewardXp,
                        UpdatedAtUtc = nowUtc
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                break;
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception) && retry < 2)
            {
                db.ChangeTracker.Clear();
            }
        }

        var rows = await db.UserMissionProgress.AsNoTracking()
            .Where(item => item.UserId == userId &&
                           ((item.Scope == MissionScope.Daily && item.PeriodStartLocal == todayLocal) ||
                            (item.Scope == MissionScope.Weekly && item.PeriodStartLocal == weekStartLocal)))
            .ToListAsync(cancellationToken);

        var rowMap = rows.ToDictionary(item => (item.MissionCode, item.PeriodStartLocal));
        var daily = new List<MissionStatusItemDto>();
        var weekly = new List<MissionStatusItemDto>();

        foreach (var template in GamificationDefaults.MissionCatalog.OrderBy(item => item.SortOrder))
        {
            var periodStart = template.Scope == MissionScope.Daily ? todayLocal : weekStartLocal;
            rowMap.TryGetValue((template.Code, periodStart), out var row);
            var currentVal = row?.CurrentValue ?? 0;
            var isCompleted = row?.CompletedAtUtc is not null;
            var effectiveCurrent = isCompleted ? template.TargetValue : Math.Min(currentVal, template.TargetValue);
            var percent = template.TargetValue <= 0
                ? 100
                : Math.Clamp((int)Math.Round(effectiveCurrent * 100.0 / template.TargetValue, MidpointRounding.AwayFromZero), 0, 100);

            var dto = new MissionStatusItemDto(
                template.Code,
                GamificationCalculator.FormatMissionScope(template.Scope),
                template.Title,
                template.Description,
                template.IconPath,
                effectiveCurrent,
                template.TargetValue,
                percent,
                template.RewardXp,
                isCompleted,
                row?.CompletedAtUtc,
                periodStart.ToString("yyyy-MM-dd"));

            if (template.Scope == MissionScope.Daily)
                daily.Add(dto);
            else
                weekly.Add(dto);
        }

        return TypedResults.Ok(new MissionsOverviewResponse(
            todayLocal.ToString("yyyy-MM-dd"),
            weekStartLocal.ToString("yyyy-MM-dd"),
            weekEndLocal.ToString("yyyy-MM-dd"),
            streak?.TimeZoneId ?? GamificationDefaults.DefaultTimeZoneId,
            daily,
            weekly));
    }

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 2601 or 2627 })
                return true;
        }
        return false;
    }
}

public sealed record RecentXpLedgerItemDto(
    Guid Id,
    string EventCode,
    string Label,
    int Amount,
    string LocalActivityDate,
    DateTimeOffset CreatedAtUtc);

public sealed record GamificationSummaryResponse(
    int TotalXp,
    bool IsReconciledWithLedger,
    int TodayXp,
    int WeeklyXp,
    int RewardedReviewsToday,
    int DailyRewardedReviewCap,
    int CurrentLevel,
    string LevelTitle,
    int CurrentLevelMinXp,
    int? NextLevelMinXp,
    int XpIntoLevel,
    int? XpNeededForNextLevel,
    int LevelProgressPercent,
    int CurrentStreak,
    int BestStreak,
    bool StudiedToday,
    string? LastActivityDateLocal,
    string TimeZoneId,
    int UniqueLessonsCompleted,
    int TotalLessonCompletions,
    int PerfectLessonsCount,
    int ConsecutiveCorrectAnswers,
    int BestConsecutiveCorrectAnswers,
    int UnlockedAchievementsCount,
    int TotalAchievementsCount,
    IReadOnlyList<RecentXpLedgerItemDto> RecentXpEvents,
    HeartsStatusDto Hearts);

public sealed record MissionStatusItemDto(
    string Code,
    string Scope,
    string Title,
    string Description,
    string IconPath,
    int CurrentValue,
    int TargetValue,
    int ProgressPercent,
    int RewardXp,
    bool IsCompleted,
    DateTimeOffset? CompletedAtUtc,
    string PeriodStartLocal);

public sealed record MissionsOverviewResponse(
    string TodayDateLocal,
    string WeekStartDateLocal,
    string WeekEndDateLocal,
    string TimeZoneId,
    IReadOnlyList<MissionStatusItemDto> DailyMissions,
    IReadOnlyList<MissionStatusItemDto> WeeklyMissions);

public sealed record AchievementStatusItemDto(
    string Code,
    string Name,
    string Description,
    string Category,
    string Rarity,
    string IconPath,
    bool IsUnlocked,
    DateTimeOffset? UnlockedAtUtc,
    int CurrentProgress,
    int TargetProgress,
    int ProgressPercent);

public sealed record AchievementsCatalogResponse(
    int UnlockedCount,
    int TotalCount,
    IReadOnlyList<AchievementStatusItemDto> Achievements);
