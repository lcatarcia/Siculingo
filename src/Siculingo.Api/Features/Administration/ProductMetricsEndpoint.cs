using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Gamification;
using Siculingo.Domain.Administration;
using Siculingo.Domain.Gamification;
using Siculingo.Domain.Learning;
using Siculingo.Domain.Social;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Administration;

public static class ProductMetricsEndpoint
{
    public static async Task<Ok<ProductMetricsResponse>> HandleAsync(
        int? windowDays,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var days = Math.Clamp(windowDays ?? 30, 7, 90);
        var nowUtc = clock.GetUtcNow();
        var windowStartUtc = nowUtc.AddDays(-days);
        var timeZone = GamificationCalculator.ResolveTimeZone(GamificationDefaults.DefaultTimeZoneId);
        var todayLocal = GamificationCalculator.ToLocalDate(nowUtc, timeZone);

        var totalRegisteredUsers = await db.Users.AsNoTracking().CountAsync(cancellationToken);
        var userRegistrations = await db.Users.AsNoTracking()
            .Where(u => u.RegisteredAtUtc >= windowStartUtc)
            .Select(u => new { u.Id, u.RegisteredAtUtc })
            .ToListAsync(cancellationToken);

        var attemptsInWindow = await db.LessonAttempts.AsNoTracking()
            .Where(a => a.StartedAtUtc >= windowStartUtc || (a.CompletedAtUtc.HasValue && a.CompletedAtUtc.Value >= windowStartUtc))
            .Select(a => new
            {
                a.Id,
                a.UserId,
                a.LessonId,
                a.IsPractice,
                a.Status,
                a.StartedAtUtc,
                a.CompletedAtUtc
            })
            .ToListAsync(cancellationToken);

        var startedInWindow = attemptsInWindow.Where(a => a.StartedAtUtc >= windowStartUtc).ToList();
        var completedInWindow = attemptsInWindow
            .Where(a => a.Status == LessonAttemptStatus.Completed && a.CompletedAtUtc.HasValue && a.CompletedAtUtc.Value >= windowStartUtc)
            .ToList();

        var learnersStartedCount = startedInWindow.Select(a => a.UserId).Distinct().Count();
        var lessonsStartedCount = startedInWindow.Count;
        var lessonsCompletedCount = completedInWindow.Count;
        var completionRatePercent = lessonsStartedCount > 0
            ? Math.Round(lessonsCompletedCount * 100.0 / lessonsStartedCount, 1)
            : 0.0;

        var avgCompletedLessonsPerLearner = learnersStartedCount > 0
            ? Math.Round((double)lessonsCompletedCount / learnersStartedCount, 2)
            : 0.0;

        // Lesson duration vs 3-5 min target (exclude > 60m idle tabs from median calculation)
        var validDurationsSec = completedInWindow
            .Select(a => (a.CompletedAtUtc!.Value - a.StartedAtUtc).TotalSeconds)
            .Where(sec => sec is >= 1 and <= 3600)
            .OrderBy(sec => sec)
            .ToList();

        double? medianDurationSeconds = null;
        var withinTargetPercent = 0.0;
        if (validDurationsSec.Count > 0)
        {
            var mid = validDurationsSec.Count / 2;
            var median = validDurationsSec.Count % 2 == 1
                ? validDurationsSec[mid]
                : (validDurationsSec[mid - 1] + validDurationsSec[mid]) / 2.0;
            medianDurationSeconds = Math.Round(median, 1);

            var withinTarget = validDurationsSec.Count(sec => sec is >= 180 and <= 300);
            withinTargetPercent = Math.Round(withinTarget * 100.0 / validDurationsSec.Count, 1);
        }

        // Practice / review adoption
        var practiceCompletedCount = completedInWindow.Count(a => a.IsPractice);
        var learnersUsingPractice = completedInWindow
            .Where(a => a.IsPractice)
            .Select(a => a.UserId)
            .Distinct()
            .Count();
        var practiceAdoptionPercent = learnersStartedCount > 0
            ? Math.Round(learnersUsingPractice * 100.0 / learnersStartedCount, 1)
            : 0.0;

        // D1 and D7 cohort retention in Europe/Rome local calendar days
        var cohortUserIds = userRegistrations.Select(u => u.Id).ToHashSet();
        var cohortActivityRows = cohortUserIds.Count > 0
            ? await db.LessonAttempts.AsNoTracking()
                .Where(a => cohortUserIds.Contains(a.UserId))
                .Select(a => new { a.UserId, ActivityUtc = a.CompletedAtUtc ?? a.StartedAtUtc })
                .ToListAsync(cancellationToken)
            : [];

        var activityDatesByUser = cohortActivityRows
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => GamificationCalculator.ToLocalDate(x.ActivityUtc, timeZone)).ToHashSet());

        var d1Eligible = userRegistrations
            .Select(u => new { u.Id, RegDate = GamificationCalculator.ToLocalDate(u.RegisteredAtUtc, timeZone) })
            .Where(u => u.RegDate.AddDays(1) <= todayLocal)
            .ToList();
        var d1Returned = d1Eligible.Count(u =>
            activityDatesByUser.TryGetValue(u.Id, out var dates) && dates.Contains(u.RegDate.AddDays(1)));
        var d1RatePercent = d1Eligible.Count > 0
            ? Math.Round(d1Returned * 100.0 / d1Eligible.Count, 1)
            : 0.0;

        var d7Eligible = userRegistrations
            .Select(u => new { u.Id, RegDate = GamificationCalculator.ToLocalDate(u.RegisteredAtUtc, timeZone) })
            .Where(u => u.RegDate.AddDays(7) <= todayLocal)
            .ToList();
        var d7Returned = d7Eligible.Count(u =>
            activityDatesByUser.TryGetValue(u.Id, out var dates) && dates.Contains(u.RegDate.AddDays(7)));
        var d7RatePercent = d7Eligible.Count > 0
            ? Math.Round(d7Returned * 100.0 / d7Eligible.Count, 1)
            : 0.0;

        // Social / friend adoption
        var acceptedFriendPairs = await db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted)
            .Select(f => new { f.UserLowId, f.UserHighId })
            .ToListAsync(cancellationToken);

        var usersWithFriends = acceptedFriendPairs
            .SelectMany(f => new[] { f.UserLowId, f.UserHighId })
            .Distinct()
            .Count();
        var friendAdoptionPercent = totalRegisteredUsers > 0
            ? Math.Round(usersWithFriends * 100.0 / totalRegisteredUsers, 1)
            : 0.0;

        // Aggregate exercise accuracy + linguistic report correlation (no user identifiers exposed)
        var rawAnswerStats = await db.LessonAttemptAnswers.AsNoTracking()
            .Where(ans => ans.AnsweredAtUtc >= windowStartUtc)
            .Join(
                db.LessonAttempts.AsNoTracking(),
                ans => ans.LessonAttemptId,
                att => att.Id,
                (ans, att) => new { att.LessonId, ans.ExerciseId, ans.IsCorrect })
            .GroupBy(x => new { x.LessonId, x.ExerciseId })
            .Select(g => new
            {
                g.Key.LessonId,
                g.Key.ExerciseId,
                TotalAnswers = g.Count(),
                CorrectAnswers = g.Count(x => x.IsCorrect)
            })
            .ToListAsync(cancellationToken);

        var lessonTitles = await db.Lessons.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => l.Title, cancellationToken);

        var openReportCounts = await db.UserReports.AsNoTracking()
            .Where(r => r.Kind == ReportKind.ContentExercise
                        && r.TargetLessonId.HasValue
                        && r.TargetExerciseId != null
                        && (r.Status == ReportStatus.Open || r.Status == ReportStatus.InReview))
            .GroupBy(r => new { LessonId = r.TargetLessonId!.Value, ExerciseId = r.TargetExerciseId! })
            .Select(g => new { g.Key.LessonId, g.Key.ExerciseId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var openReportsLookup = openReportCounts
            .ToDictionary(x => (x.LessonId, x.ExerciseId), x => x.Count);

        var overallAnswers = rawAnswerStats.Sum(x => x.TotalAnswers);
        var overallCorrect = rawAnswerStats.Sum(x => x.CorrectAnswers);
        var overallAccuracyPercent = overallAnswers > 0
            ? Math.Round(overallCorrect * 100.0 / overallAnswers, 1)
            : 0.0;

        var exerciseHotspots = rawAnswerStats
            .Select(x =>
            {
                var accuracy = x.TotalAnswers > 0 ? Math.Round(x.CorrectAnswers * 100.0 / x.TotalAnswers, 1) : 0.0;
                openReportsLookup.TryGetValue((x.LessonId, x.ExerciseId), out var openReports);
                return new ExerciseAccuracyMetricDto(
                    x.LessonId,
                    lessonTitles.GetValueOrDefault(x.LessonId, "Lezione"),
                    x.ExerciseId,
                    x.TotalAnswers,
                    x.CorrectAnswers,
                    accuracy,
                    openReports);
            })
            .OrderBy(x => x.AccuracyPercent)
            .ThenByDescending(x => x.OpenReportsCount)
            .ThenByDescending(x => x.TotalAnswers)
            .Take(25)
            .ToList();

        return TypedResults.Ok(new ProductMetricsResponse(
            days,
            windowStartUtc,
            nowUtc,
            "Metriche pseudonimizzate interne prive di tracker di terze parti. Con 20-50 tester i dati sono esplorativi: valutare errori, durata reale (obiettivo 180-300 s) e segnalazioni linguistiche insieme ai completamenti, senza considerare gli XP prova di apprendimento.",
            totalRegisteredUsers,
            userRegistrations.Count,
            learnersStartedCount,
            lessonsStartedCount,
            lessonsCompletedCount,
            completionRatePercent,
            avgCompletedLessonsPerLearner,
            medianDurationSeconds,
            validDurationsSec.Count,
            withinTargetPercent,
            overallAnswers,
            overallAccuracyPercent,
            practiceCompletedCount,
            learnersUsingPractice,
            practiceAdoptionPercent,
            d1Eligible.Count,
            d1Returned,
            d1RatePercent,
            d7Eligible.Count,
            d7Returned,
            d7RatePercent,
            usersWithFriends,
            friendAdoptionPercent,
            exerciseHotspots));
    }
}

public sealed record ExerciseAccuracyMetricDto(
    Guid LessonId,
    string LessonTitle,
    string ExerciseId,
    int TotalAnswers,
    int CorrectAnswers,
    double AccuracyPercent,
    int OpenReportsCount);

public sealed record ProductMetricsResponse(
    int WindowDays,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string MethodologyNote,
    int TotalRegisteredUsers,
    int RegisteredUsersInWindow,
    int ActiveLearnersDenominator,
    int LessonsStartedInWindow,
    int LessonsCompletedInWindow,
    double LessonCompletionRatePercent,
    double AverageCompletedLessonsPerActiveLearner,
    double? MedianLessonDurationSeconds,
    int DurationMeasuredAttemptsCount,
    double ShareWithinThreeToFiveMinutesPercent,
    int TotalExerciseAnswersInWindow,
    double OverallExerciseAccuracyPercent,
    int PracticeCompletionsInWindow,
    int LearnersUsingPracticeInWindow,
    double PracticeAdoptionRatePercent,
    int D1EligibleCohortCount,
    int D1ReturnedCount,
    double D1ReturnRatePercent,
    int D7EligibleCohortCount,
    int D7ReturnedCount,
    double D7ReturnRatePercent,
    int UsersWithAcceptedFriendsCount,
    double FriendAdoptionRatePercent,
    IReadOnlyList<ExerciseAccuracyMetricDto> ExerciseAccuracyItems);
