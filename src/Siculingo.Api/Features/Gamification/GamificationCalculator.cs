using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Social;
using Siculingo.Domain.Gamification;
using Siculingo.Domain.Learning;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Gamification;

public static class GamificationCalculator
{
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var requested = string.IsNullOrWhiteSpace(timeZoneId)
            ? GamificationDefaults.DefaultTimeZoneId
            : timeZoneId.Trim();

        if (TimeZoneInfo.TryFindSystemTimeZoneById(requested, out var resolved))
            return resolved;

        // Fallback mapping between IANA Europe/Rome and Windows W. Europe Standard Time if needed
        if (string.Equals(requested, "Europe/Rome", StringComparison.OrdinalIgnoreCase) &&
            TimeZoneInfo.TryFindSystemTimeZoneById("W. Europe Standard Time", out var windowsRome))
        {
            return windowsRome;
        }

        return TimeZoneInfo.Utc;
    }

    public static DateOnly ToLocalDate(DateTimeOffset utcTimestamp, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(utcTimestamp, timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public static DateOnly GetWeekStartMonday(DateOnly localDate)
    {
        var daysSinceMonday = ((int)localDate.DayOfWeek + 6) % 7;
        return localDate.AddDays(-daysSinceMonday);
    }

    public static int CalculateEffectiveCurrentStreak(UserStreak? streak, DateOnly todayLocal)
    {
        if (streak is null || streak.LastActivityDateLocal is null || streak.CurrentStreak <= 0)
            return 0;

        var daysElapsed = todayLocal.DayNumber - streak.LastActivityDateLocal.Value.DayNumber;
        return daysElapsed <= 1 ? streak.CurrentStreak : 0;
    }

    public static LevelProgressSnapshot CalculateLevelProgress(int totalXp, IReadOnlyList<LevelThreshold> orderedThresholds)
    {
        if (orderedThresholds.Count == 0)
        {
            return new LevelProgressSnapshot(1, "Curioso", totalXp, 0, null, totalXp, null, 100);
        }

        var currentIndex = 0;
        for (var index = 0; index < orderedThresholds.Count; index++)
        {
            if (totalXp >= orderedThresholds[index].MinXp)
                currentIndex = index;
            else
                break;
        }

        var current = orderedThresholds[currentIndex];
        var next = currentIndex + 1 < orderedThresholds.Count ? orderedThresholds[currentIndex + 1] : null;
        if (next is null)
        {
            return new LevelProgressSnapshot(
                current.Level,
                current.Title,
                totalXp,
                current.MinXp,
                null,
                totalXp - current.MinXp,
                null,
                100);
        }

        var xpIntoLevel = Math.Max(0, totalXp - current.MinXp);
        var span = Math.Max(1, next.MinXp - current.MinXp);
        var xpNeededForNext = Math.Max(0, next.MinXp - totalXp);
        var percent = Math.Clamp((int)Math.Round(xpIntoLevel * 100.0 / span, MidpointRounding.AwayFromZero), 0, 100);

        return new LevelProgressSnapshot(
            current.Level,
            current.Title,
            totalXp,
            current.MinXp,
            next.MinXp,
            xpIntoLevel,
            xpNeededForNext,
            percent);
    }

    public static async Task ApplyAnswerPrecisionProgressAsync(
        SiculingoDbContext db,
        Guid userId,
        bool isCorrect,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var stats = await GetOrCreateTrackedStatsAsync(db, userId, nowUtc, cancellationToken);
        if (isCorrect)
        {
            stats.ConsecutiveCorrectAnswers += 1;
            if (stats.ConsecutiveCorrectAnswers > stats.BestConsecutiveCorrectAnswers)
                stats.BestConsecutiveCorrectAnswers = stats.ConsecutiveCorrectAnswers;
        }
        else
        {
            stats.ConsecutiveCorrectAnswers = 0;
        }

        stats.UpdatedAtUtc = nowUtc;
    }

    public static async Task<CompletionRewardOutcome> ApplyAttemptCompletionRewardsAsync(
        SiculingoDbContext db,
        LessonAttempt attempt,
        bool isFirstLessonCompletion,
        int correctCount,
        int totalCount,
        DateTimeOffset completedAtUtc,
        bool heartsConsumptionEnabled,
        CancellationToken cancellationToken)
    {
        var stats = await GetOrCreateTrackedStatsAsync(db, attempt.UserId, completedAtUtc, cancellationToken);
        var streak = await GetOrCreateTrackedStreakAsync(db, attempt.UserId, completedAtUtc, cancellationToken);
        var thresholds = await db.LevelThresholds.AsNoTracking()
            .OrderBy(item => item.Level)
            .ToListAsync(cancellationToken);

        var previousLevel = stats.CurrentLevel;
        var timeZone = ResolveTimeZone(streak.TimeZoneId);
        var activityDateLocal = ToLocalDate(completedAtUtc, timeZone);
        var weekStartLocal = GetWeekStartMonday(activityDateLocal);

        var isPerfectLesson = totalCount > 0 && correctCount == totalCount;
        var isFirstActivityToday = streak.LastActivityDateLocal != activityDateLocal;

        var awardedEntries = new List<XpLedgerEntry>();

        void AddXp(XpEventType eventType, int amount, string suffix)
        {
            if (amount <= 0)
                return;

            var entry = new XpLedgerEntry
            {
                UserId = attempt.UserId,
                EventType = eventType,
                SourceAttemptId = attempt.Id,
                SourceLessonId = attempt.LessonId,
                Amount = amount,
                LocalActivityDate = activityDateLocal,
                CreatedAtUtc = completedAtUtc,
                IdempotencyKey = $"attempt:{attempt.Id:N}:{suffix}"
            };
            db.XpLedger.Add(entry);
            awardedEntries.Add(entry);
        }

        var dailyReviewCapReached = false;
        if (isFirstLessonCompletion && !attempt.IsPractice)
        {
            AddXp(XpEventType.CorrectExercises, correctCount * GamificationDefaults.CorrectExerciseXp, "correct-exercises");
            AddXp(XpEventType.LessonFirstCompletion, GamificationDefaults.FirstLessonCompletionXp, "lesson-first-completion");
        }
        else
        {
            var rewardedReviewsToday = await db.XpLedger
                .CountAsync(
                    entry => entry.UserId == attempt.UserId &&
                             entry.LocalActivityDate == activityDateLocal &&
                             entry.EventType == XpEventType.ReviewCompletion,
                    cancellationToken);

            if (rewardedReviewsToday < GamificationDefaults.DailyRewardedReviewCap)
            {
                AddXp(XpEventType.ReviewCompletion, GamificationDefaults.ReviewCompletionXp, "review-completion");
            }
            else
            {
                dailyReviewCapReached = true;
            }
        }

        if (isPerfectLesson)
        {
            AddXp(XpEventType.PerfectLessonBonus, GamificationDefaults.PerfectLessonBonusXp, "perfect-lesson");
        }

        if (isFirstActivityToday)
        {
            AddXp(XpEventType.FirstActivityOfDayBonus, GamificationDefaults.FirstActivityOfDayBonusXp, "first-activity-of-day");
        }

        // Practice/review restores +1 heart up to MaxHearts
        HeartsStatusDto heartsStatus;
        var practiceHeartRestored = false;
        if (attempt.IsPractice)
        {
            (practiceHeartRestored, heartsStatus) = await HeartsService.RestoreHeartOnPracticeCompletionAsync(
                db,
                attempt.UserId,
                completedAtUtc,
                heartsConsumptionEnabled,
                cancellationToken);
        }
        else
        {
            var trackedHearts = await HeartsService.GetOrCreateNormalizedTrackedAsync(
                db,
                attempt.UserId,
                completedAtUtc,
                cancellationToken);
            heartsStatus = HeartsService.BuildStatusDto(
                trackedHearts.CurrentHearts,
                trackedHearts.RegenerationAnchorUtc,
                completedAtUtc,
                heartsConsumptionEnabled);
        }

        // Evaluate daily and weekly missions (non-recursive XP bonuses, idempotent by mission+period)
        var newlyCompletedMissions = await EvaluateAndAwardMissionsTrackedAsync(
            db,
            attempt,
            timeZone,
            activityDateLocal,
            weekStartLocal,
            correctCount,
            isPerfectLesson,
            awardedEntries,
            completedAtUtc,
            cancellationToken);

        var totalAwardedXp = awardedEntries.Sum(item => item.Amount);

        // Update streak
        var streakIncremented = false;
        if (streak.LastActivityDateLocal is null)
        {
            streak.CurrentStreak = 1;
            streak.BestStreak = Math.Max(streak.BestStreak, 1);
            streak.LastActivityDateLocal = activityDateLocal;
            streakIncremented = true;
        }
        else if (activityDateLocal > streak.LastActivityDateLocal.Value)
        {
            var dayDelta = activityDateLocal.DayNumber - streak.LastActivityDateLocal.Value.DayNumber;
            streak.CurrentStreak = dayDelta == 1 ? streak.CurrentStreak + 1 : 1;
            if (streak.CurrentStreak > streak.BestStreak)
                streak.BestStreak = streak.CurrentStreak;
            streak.LastActivityDateLocal = activityDateLocal;
            streakIncremented = true;
        }
        // If activityDateLocal <= LastActivityDateLocal, keep streak unchanged (same-day multiple lessons or out-of-order clock skew).
        streak.UpdatedAtUtc = completedAtUtc;

        // Update UserStats reconciled with ledger + completions
        var priorLedgerTotal = await db.XpLedger
            .Where(entry => entry.UserId == attempt.UserId)
            .SumAsync(entry => (int?)entry.Amount, cancellationToken) ?? 0;

        stats.TotalXp = priorLedgerTotal + totalAwardedXp;
        var levelProgress = CalculateLevelProgress(stats.TotalXp, thresholds);
        stats.CurrentLevel = levelProgress.CurrentLevel;
        if (isFirstLessonCompletion && !attempt.IsPractice)
            stats.UniqueLessonsCompleted += 1;
        stats.TotalLessonCompletions += 1;
        if (isPerfectLesson)
            stats.PerfectLessonsCount += 1;
        stats.UpdatedAtUtc = completedAtUtc;

        // Ensure weekly league participation row exists for the current week
        await LeagueService.EnsureEnrolledInWeekTrackedAsync(
            db,
            attempt.UserId,
            weekStartLocal,
            completedAtUtc,
            cancellationToken);

        // Evaluate base achievements
        var newlyUnlocked = await EvaluateAndUnlockAchievementsAsync(
            db,
            attempt,
            stats,
            streak,
            isFirstLessonCompletion && !attempt.IsPractice,
            completedAtUtc,
            cancellationToken);

        return BuildRewardOutcome(
            awardedEntries,
            dailyReviewCapReached,
            previousLevel,
            levelProgress,
            streak.CurrentStreak,
            streak.BestStreak,
            streakIncremented,
            newlyUnlocked,
            newlyCompletedMissions,
            practiceHeartRestored,
            heartsStatus);
    }

    public static async Task<CompletionRewardOutcome> ReadAttemptCompletionRewardsAsync(
        SiculingoDbContext db,
        LessonAttempt attempt,
        DateTimeOffset nowUtc,
        bool heartsConsumptionEnabled,
        CancellationToken cancellationToken)
    {
        var entries = await db.XpLedger.AsNoTracking()
            .Where(entry => entry.UserId == attempt.UserId && entry.SourceAttemptId == attempt.Id)
            .OrderBy(entry => entry.EventType)
            .ToListAsync(cancellationToken);

        var stats = await db.UserStats.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == attempt.UserId, cancellationToken);
        var streak = await db.UserStreaks.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == attempt.UserId, cancellationToken);
        var thresholds = await db.LevelThresholds.AsNoTracking()
            .OrderBy(item => item.Level)
            .ToListAsync(cancellationToken);

        var unlockedRows = await db.UserAchievements.AsNoTracking()
            .Where(item => item.UserId == attempt.UserId && item.SourceAttemptId == attempt.Id)
            .Join(
                db.Achievements.AsNoTracking(),
                ua => ua.AchievementId,
                def => def.Id,
                (ua, def) => new
                {
                    def.Code,
                    def.Name,
                    def.Description,
                    def.Category,
                    def.Rarity,
                    def.IconPath,
                    ua.UnlockedAtUtc
                })
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);

        var unlockedOnAttempt = unlockedRows
            .Select(item => new UnlockedAchievementDto(
                item.Code,
                item.Name,
                item.Description,
                FormatCategory(item.Category),
                FormatRarity(item.Rarity),
                item.IconPath,
                item.UnlockedAtUtc))
            .ToList();

        var totalXp = stats?.TotalXp ?? 0;
        var levelProgress = CalculateLevelProgress(totalXp, thresholds);
        var awardedSum = entries.Sum(item => item.Amount);
        var previousXp = Math.Max(0, totalXp - awardedSum);
        var previousLevel = CalculateLevelProgress(previousXp, thresholds).CurrentLevel;

        var dailyReviewCapReached = entries.Count == 0 ||
            entries.All(item => item.EventType is not XpEventType.LessonFirstCompletion and not XpEventType.ReviewCompletion);

        var entryIds = entries.Select(item => (Guid?)item.Id).ToList();
        var completedMissionRows = await db.UserMissionProgress.AsNoTracking()
            .Where(item => item.UserId == attempt.UserId && item.RewardLedgerEntryId != null && entryIds.Contains(item.RewardLedgerEntryId))
            .ToListAsync(cancellationToken);

        var completedMissions = completedMissionRows
            .Select(row =>
            {
                var template = GamificationDefaults.MissionCatalog.FirstOrDefault(t => t.Code == row.MissionCode);
                return new CompletedMissionDto(
                    row.MissionCode,
                    template?.Title ?? row.MissionCode,
                    FormatMissionScope(row.Scope),
                    row.RewardXp,
                    row.CompletedAtUtc ?? attempt.CompletedAtUtc ?? nowUtc);
            })
            .ToList();

        var heartsStatus = await HeartsService.GetStatusSnapshotAsync(
            db,
            attempt.UserId,
            nowUtc,
            heartsConsumptionEnabled,
            cancellationToken);

        return BuildRewardOutcome(
            entries,
            dailyReviewCapReached,
            previousLevel,
            levelProgress,
            streak?.CurrentStreak ?? 0,
            streak?.BestStreak ?? 0,
            streakIncremented: false,
            unlockedOnAttempt,
            completedMissions,
            practiceHeartRestored: false,
            heartsStatus);
    }

    private static async Task<List<CompletedMissionDto>> EvaluateAndAwardMissionsTrackedAsync(
        SiculingoDbContext db,
        LessonAttempt currentAttempt,
        TimeZoneInfo timeZone,
        DateOnly todayLocal,
        DateOnly weekStartLocal,
        int currentAttemptCorrectCount,
        bool currentAttemptIsPerfect,
        List<XpLedgerEntry> awardedEntries,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var weekEndLocal = weekStartLocal.AddDays(6);
        var windowStartUtc = new DateTimeOffset(weekStartLocal.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-2);

        var savedWeekAttempts = await db.LessonAttempts.AsNoTracking()
            .Where(item => item.UserId == currentAttempt.UserId &&
                           item.Status == LessonAttemptStatus.Completed &&
                           item.Id != currentAttempt.Id &&
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

        var completionsToday = 1;
        var correctAnswersToday = currentAttemptCorrectCount;
        var completionsThisWeek = 1;
        var perfectLessonsThisWeek = currentAttemptIsPerfect ? 1 : 0;

        foreach (var saved in savedWeekAttempts)
        {
            var localDate = ToLocalDate(saved.CompletedAtUtc, timeZone);
            if (localDate < weekStartLocal || localDate > weekEndLocal)
                continue;

            completionsThisWeek += 1;
            if (saved.ExerciseCount > 0 && saved.TotalAnswers == saved.ExerciseCount && saved.CorrectCount == saved.ExerciseCount)
                perfectLessonsThisWeek += 1;

            if (localDate == todayLocal)
            {
                completionsToday += 1;
                correctAnswersToday += saved.CorrectCount;
            }
        }

        var priorNonMissionXpToday = await db.XpLedger
            .Where(entry => entry.UserId == currentAttempt.UserId &&
                            entry.LocalActivityDate == todayLocal &&
                            entry.EventType != XpEventType.DailyMissionBonus &&
                            entry.EventType != XpEventType.WeeklyMissionBonus)
            .SumAsync(entry => (int?)entry.Amount, cancellationToken) ?? 0;

        var currentNonMissionXp = awardedEntries
            .Where(entry => entry.EventType is not XpEventType.DailyMissionBonus and not XpEventType.WeeklyMissionBonus)
            .Sum(entry => entry.Amount);

        var lessonXpToday = priorNonMissionXpToday + currentNonMissionXp;

        var existingProgress = await db.UserMissionProgress
            .Where(item => item.UserId == currentAttempt.UserId &&
                           (item.PeriodStartLocal == todayLocal || item.PeriodStartLocal == weekStartLocal))
            .ToListAsync(cancellationToken);

        var newlyCompleted = new List<CompletedMissionDto>();

        foreach (var template in GamificationDefaults.MissionCatalog)
        {
            var periodStart = template.Scope == MissionScope.Daily ? todayLocal : weekStartLocal;
            var rawValue = template.Code switch
            {
                GamificationDefaults.MissionCodes.DailyLessonOne => completionsToday,
                GamificationDefaults.MissionCodes.DailyXpTwentyFive => lessonXpToday,
                GamificationDefaults.MissionCodes.DailyCorrectTen => correctAnswersToday,
                GamificationDefaults.MissionCodes.WeeklyLessonsFour => completionsThisWeek,
                GamificationDefaults.MissionCodes.WeeklyPerfectTwo => perfectLessonsThisWeek,
                _ => 0
            };

            var cappedValue = Math.Min(rawValue, template.TargetValue);
            var row = existingProgress.SingleOrDefault(
                item => item.MissionCode == template.Code && item.PeriodStartLocal == periodStart);

            if (row is null)
            {
                row = new UserMissionProgress
                {
                    UserId = currentAttempt.UserId,
                    Scope = template.Scope,
                    MissionCode = template.Code,
                    PeriodStartLocal = periodStart,
                    CurrentValue = cappedValue,
                    TargetValue = template.TargetValue,
                    RewardXp = template.RewardXp,
                    UpdatedAtUtc = nowUtc
                };
                db.UserMissionProgress.Add(row);
                existingProgress.Add(row);
            }
            else
            {
                row.CurrentValue = Math.Max(row.CurrentValue, cappedValue);
                row.UpdatedAtUtc = nowUtc;
            }

            if (row.CompletedAtUtc is null && row.CurrentValue >= template.TargetValue)
            {
                var eventType = template.Scope == MissionScope.Daily
                    ? XpEventType.DailyMissionBonus
                    : XpEventType.WeeklyMissionBonus;

                var rewardEntry = new XpLedgerEntry
                {
                    UserId = currentAttempt.UserId,
                    EventType = eventType,
                    SourceAttemptId = currentAttempt.Id,
                    SourceLessonId = currentAttempt.LessonId,
                    Amount = template.RewardXp,
                    LocalActivityDate = todayLocal,
                    CreatedAtUtc = nowUtc,
                    IdempotencyKey = $"mission:{currentAttempt.UserId:N}:{template.Code}:{periodStart:yyyy-MM-dd}"
                };

                db.XpLedger.Add(rewardEntry);
                awardedEntries.Add(rewardEntry);
                row.CompletedAtUtc = nowUtc;
                row.RewardLedgerEntryId = rewardEntry.Id;

                newlyCompleted.Add(new CompletedMissionDto(
                    template.Code,
                    template.Title,
                    FormatMissionScope(template.Scope),
                    template.RewardXp,
                    nowUtc));
            }
        }

        return newlyCompleted;
    }

    public static string FormatMissionScope(MissionScope scope) => scope switch
    {
        MissionScope.Daily => "daily",
        MissionScope.Weekly => "weekly",
        _ => "daily"
    };

    private static async Task<List<UnlockedAchievementDto>> EvaluateAndUnlockAchievementsAsync(
        SiculingoDbContext db,
        LessonAttempt attempt,
        UserStats stats,
        UserStreak streak,
        bool isFirstLessonCompletion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var activeDefinitions = await db.Achievements.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);

        var alreadyUnlockedIds = (await db.UserAchievements.AsNoTracking()
            .Where(item => item.UserId == attempt.UserId)
            .Select(item => item.AchievementId)
            .ToListAsync(cancellationToken)).ToHashSet();

        // Check whether any published unit is completely finished by this user
        var completedLessonIds = (await db.UserLessonCompletions.AsNoTracking()
            .Where(item => item.UserId == attempt.UserId)
            .Select(item => item.LessonId)
            .ToListAsync(cancellationToken)).ToHashSet();
        if (isFirstLessonCompletion)
            completedLessonIds.Add(attempt.LessonId);

        var hasCompletedFullPublishedUnit = await HasCompletedAnyFullPublishedUnitAsync(db, completedLessonIds, cancellationToken);

        var unlocked = new List<UnlockedAchievementDto>();
        foreach (var definition in activeDefinitions)
        {
            if (alreadyUnlockedIds.Contains(definition.Id))
                continue;

            var qualifies = definition.Code switch
            {
                GamificationDefaults.AchievementCodes.FirstLesson => stats.TotalLessonCompletions >= definition.Threshold,
                GamificationDefaults.AchievementCodes.PerfectLesson => stats.PerfectLessonsCount >= definition.Threshold,
                GamificationDefaults.AchievementCodes.PrecisionTen => stats.BestConsecutiveCorrectAnswers >= definition.Threshold,
                GamificationDefaults.AchievementCodes.StreakThree => streak.BestStreak >= definition.Threshold,
                GamificationDefaults.AchievementCodes.StreakSeven => streak.BestStreak >= definition.Threshold,
                GamificationDefaults.AchievementCodes.PilotUnitComplete => hasCompletedFullPublishedUnit,
                _ => false
            };

            if (!qualifies)
                continue;

            db.UserAchievements.Add(new UserAchievement
            {
                UserId = attempt.UserId,
                AchievementId = definition.Id,
                SourceAttemptId = attempt.Id,
                UnlockedAtUtc = nowUtc
            });

            unlocked.Add(new UnlockedAchievementDto(
                definition.Code,
                definition.Name,
                definition.Description,
                FormatCategory(definition.Category),
                FormatRarity(definition.Rarity),
                definition.IconPath,
                nowUtc));
        }

        return unlocked;
    }

    public static async Task<bool> HasCompletedAnyFullPublishedUnitAsync(
        SiculingoDbContext db,
        IReadOnlySet<Guid> completedLessonIds,
        CancellationToken cancellationToken)
    {
        if (completedLessonIds.Count == 0)
            return false;

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

        return publishedUnitLessons
            .GroupBy(item => item.UnitId)
            .Any(group => group.All(item => completedLessonIds.Contains(item.Id)));
    }

    public static string FormatCategory(AchievementCategory category) => category switch
    {
        AchievementCategory.Learning => "apprendimento",
        AchievementCategory.Consistency => "costanza",
        AchievementCategory.Precision => "precisione",
        AchievementCategory.Exploration => "esplorazione",
        _ => "apprendimento"
    };

    public static string FormatRarity(AchievementRarity rarity) => rarity switch
    {
        AchievementRarity.Common => "comune",
        AchievementRarity.Rare => "raro",
        AchievementRarity.Epic => "epico",
        AchievementRarity.Legendary => "leggendario",
        _ => "comune"
    };

    public static string DescribeXpEventType(XpEventType eventType) => eventType switch
    {
        XpEventType.CorrectExercises => "Risposte corrette della lezione",
        XpEventType.LessonFirstCompletion => "Prima volta su questa lezione",
        XpEventType.PerfectLessonBonus => "Bonus zero errori",
        XpEventType.FirstActivityOfDayBonus => "Prima lezione di oggi",
        XpEventType.ReviewCompletion => "Ripasso completato",
        XpEventType.DailyMissionBonus => "Obiettivo giornaliero completato",
        XpEventType.WeeklyMissionBonus => "Obiettivo settimanale completato",
        _ => "Attività didattica"
    };

    private static string DescribeXpEvent(XpLedgerEntry entry) => DescribeXpEventType(entry.EventType);

    public static string FormatEventCode(XpEventType eventType) => eventType switch
    {
        XpEventType.CorrectExercises => "correctExercises",
        XpEventType.LessonFirstCompletion => "lessonFirstCompletion",
        XpEventType.PerfectLessonBonus => "perfectLessonBonus",
        XpEventType.FirstActivityOfDayBonus => "firstActivityOfDayBonus",
        XpEventType.ReviewCompletion => "reviewCompletion",
        XpEventType.DailyMissionBonus => "dailyMissionBonus",
        XpEventType.WeeklyMissionBonus => "weeklyMissionBonus",
        _ => "other"
    };

    private static CompletionRewardOutcome BuildRewardOutcome(
        IReadOnlyList<XpLedgerEntry> entries,
        bool dailyReviewCapReached,
        int previousLevel,
        LevelProgressSnapshot levelProgress,
        int currentStreak,
        int bestStreak,
        bool streakIncremented,
        IReadOnlyList<UnlockedAchievementDto> newlyUnlocked,
        IReadOnlyList<CompletedMissionDto> newlyCompletedMissions,
        bool practiceHeartRestored,
        HeartsStatusDto hearts)
    {
        var breakdown = entries
            .Select(entry => new XpBreakdownItemDto(
                FormatEventCode(entry.EventType),
                DescribeXpEvent(entry),
                entry.Amount))
            .ToList();

        var xpEarned = entries.Sum(item => item.Amount);
        return new CompletionRewardOutcome(
            xpEarned,
            dailyReviewCapReached,
            breakdown,
            previousLevel,
            levelProgress.CurrentLevel,
            levelProgress.LevelTitle,
            levelProgress.CurrentLevel > previousLevel,
            levelProgress.TotalXp,
            levelProgress.CurrentLevelMinXp,
            levelProgress.NextLevelMinXp,
            levelProgress.ProgressPercent,
            currentStreak,
            bestStreak,
            streakIncremented,
            newlyUnlocked,
            newlyCompletedMissions,
            practiceHeartRestored,
            hearts);
    }

    private static async Task<UserStats> GetOrCreateTrackedStatsAsync(
        SiculingoDbContext db,
        Guid userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var stats = await db.UserStats.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (stats is not null)
            return stats;

        stats = new UserStats
        {
            UserId = userId,
            TotalXp = 0,
            CurrentLevel = 1,
            UpdatedAtUtc = nowUtc
        };
        db.UserStats.Add(stats);
        return stats;
    }

    private static async Task<UserStreak> GetOrCreateTrackedStreakAsync(
        SiculingoDbContext db,
        Guid userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var streak = await db.UserStreaks.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (streak is not null)
            return streak;

        streak = new UserStreak
        {
            UserId = userId,
            TimeZoneId = GamificationDefaults.DefaultTimeZoneId,
            CurrentStreak = 0,
            BestStreak = 0,
            LastActivityDateLocal = null,
            UpdatedAtUtc = nowUtc
        };
        db.UserStreaks.Add(streak);
        return streak;
    }
}

public sealed record LevelProgressSnapshot(
    int CurrentLevel,
    string LevelTitle,
    int TotalXp,
    int CurrentLevelMinXp,
    int? NextLevelMinXp,
    int XpIntoLevel,
    int? XpNeededForNextLevel,
    int ProgressPercent);

public sealed record XpBreakdownItemDto(
    string EventCode,
    string Label,
    int Amount);

public sealed record UnlockedAchievementDto(
    string Code,
    string Name,
    string Description,
    string Category,
    string Rarity,
    string IconPath,
    DateTimeOffset UnlockedAtUtc);

public sealed record CompletedMissionDto(
    string Code,
    string Title,
    string Scope,
    int RewardXp,
    DateTimeOffset CompletedAtUtc);

public sealed record CompletionRewardOutcome(
    int XpEarned,
    bool DailyReviewCapReached,
    IReadOnlyList<XpBreakdownItemDto> XpBreakdown,
    int PreviousLevel,
    int CurrentLevel,
    string LevelTitle,
    bool LeveledUp,
    int TotalXp,
    int CurrentLevelMinXp,
    int? NextLevelMinXp,
    int LevelProgressPercent,
    int CurrentStreak,
    int BestStreak,
    bool StreakIncremented,
    IReadOnlyList<UnlockedAchievementDto> NewlyUnlockedAchievements,
    IReadOnlyList<CompletedMissionDto> NewlyCompletedMissions,
    bool PracticeHeartRestored,
    HeartsStatusDto Hearts);
