using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Common;
using Siculingo.Api.Features.Gamification;
using Siculingo.Domain.Social;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Social;

public static class LeagueService
{
    public static string FormatTierCode(LeagueTier tier) => tier switch
    {
        LeagueTier.Bronze => "bronze",
        LeagueTier.Silver => "silver",
        LeagueTier.Gold => "gold",
        _ => "bronze"
    };

    public static string FormatTierName(LeagueTier tier) => tier switch
    {
        LeagueTier.Bronze => "Bronzo",
        LeagueTier.Silver => "Argento",
        LeagueTier.Gold => "Oro",
        _ => "Bronzo"
    };

    public static string FormatOutcomeCode(LeagueWeekOutcome outcome) => outcome switch
    {
        LeagueWeekOutcome.Promoted => "promoted",
        LeagueWeekOutcome.Demoted => "demoted",
        LeagueWeekOutcome.Stayed => "stayed",
        _ => "inProgress"
    };

    public static async Task EnsureEnrolledInWeekTrackedAsync(
        SiculingoDbContext db,
        Guid userId,
        DateOnly weekStartLocal,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var privacy = await db.UserPrivacySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (privacy is { ShowInLeaderboard: false })
            return;

        var profile = await db.UserLeagueProfiles
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (profile is null)
        {
            profile = new UserLeagueProfile
            {
                UserId = userId,
                CurrentTier = LeagueTier.Bronze,
                LastOutcome = LeagueWeekOutcome.InProgress,
                UpdatedAtUtc = nowUtc
            };
            db.UserLeagueProfiles.Add(profile);
        }

        var standingExists = await db.WeeklyLeagueStandings
            .AnyAsync(item => item.WeekStartLocal == weekStartLocal && item.UserId == userId, cancellationToken);
        if (!standingExists)
        {
            db.WeeklyLeagueStandings.Add(new WeeklyLeagueStanding
            {
                WeekStartLocal = weekStartLocal,
                UserId = userId,
                Tier = profile.CurrentTier,
                Outcome = LeagueWeekOutcome.InProgress,
                CreatedAtUtc = nowUtc
            });
        }
    }

    public static async Task SettleCompletedWeeksAndEnsureCurrentAsync(
        SiculingoDbContext db,
        Guid currentUserId,
        DateOnly currentWeekStart,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var hasUnsettledPrior = await db.WeeklyLeagueStandings.AsNoTracking()
            .AnyAsync(
                item => item.WeekStartLocal < currentWeekStart && item.Outcome == LeagueWeekOutcome.InProgress,
                cancellationToken);

        var currentPrivacy = await db.UserPrivacySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == currentUserId, cancellationToken);
        var participatesInLeaderboard = currentPrivacy?.ShowInLeaderboard ?? true;

        var isCurrentEnrolled = !participatesInLeaderboard || await db.WeeklyLeagueStandings.AsNoTracking()
            .AnyAsync(
                item => item.WeekStartLocal == currentWeekStart && item.UserId == currentUserId,
                cancellationToken);

        var hasProfile = await db.UserLeagueProfiles.AsNoTracking()
            .AnyAsync(item => item.UserId == currentUserId, cancellationToken);

        if (!hasUnsettledPrior && isCurrentEnrolled && hasProfile)
            return;

        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                var lockKey = hasUnsettledPrior
                    ? "siculingo:league:settle"
                    : $"siculingo:user:{currentUserId:N}";
                await using var transaction = await SqlConcurrencyLock.BeginResourceTransactionAsync(db, lockKey, cancellationToken);

                var unsettledWeeks = await db.WeeklyLeagueStandings
                    .Where(item => item.WeekStartLocal < currentWeekStart && item.Outcome == LeagueWeekOutcome.InProgress)
                    .Select(item => item.WeekStartLocal)
                    .Distinct()
                    .OrderBy(week => week)
                    .ToListAsync(cancellationToken);

                foreach (var weekStart in unsettledWeeks)
                {
                    await SettleSingleWeekTrackedAsync(db, weekStart, nowUtc, cancellationToken);
                }

                var profile = await db.UserLeagueProfiles
                    .SingleOrDefaultAsync(item => item.UserId == currentUserId, cancellationToken);
                if (profile is null)
                {
                    profile = new UserLeagueProfile
                    {
                        UserId = currentUserId,
                        CurrentTier = LeagueTier.Bronze,
                        LastOutcome = LeagueWeekOutcome.InProgress,
                        UpdatedAtUtc = nowUtc
                    };
                    db.UserLeagueProfiles.Add(profile);
                }

                if (participatesInLeaderboard)
                {
                    var currentStanding = await db.WeeklyLeagueStandings
                        .SingleOrDefaultAsync(
                            item => item.WeekStartLocal == currentWeekStart && item.UserId == currentUserId,
                            cancellationToken);

                    if (currentStanding is null)
                    {
                        db.WeeklyLeagueStandings.Add(new WeeklyLeagueStanding
                        {
                            WeekStartLocal = currentWeekStart,
                            UserId = currentUserId,
                            Tier = profile.CurrentTier,
                            Outcome = LeagueWeekOutcome.InProgress,
                            CreatedAtUtc = nowUtc
                        });
                    }
                    else if (currentStanding.Tier != profile.CurrentTier && currentStanding.Outcome == LeagueWeekOutcome.InProgress)
                    {
                        currentStanding.Tier = profile.CurrentTier;
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception) && retry < 2)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private static async Task SettleSingleWeekTrackedAsync(
        SiculingoDbContext db,
        DateOnly weekStart,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var weekEnd = weekStart.AddDays(6);
        var standings = await db.WeeklyLeagueStandings
            .Where(item => item.WeekStartLocal == weekStart && item.Outcome == LeagueWeekOutcome.InProgress)
            .ToListAsync(cancellationToken);

        if (standings.Count == 0)
            return;

        var userIds = standings.Select(item => item.UserId).ToList();

        var suspendedUserIds = (await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id) && user.IsSuspended)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hiddenUserIds = (await db.UserPrivacySettings.AsNoTracking()
            .Where(item => userIds.Contains(item.UserId) && !item.ShowInLeaderboard)
            .Select(item => item.UserId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var xpByUserId = await db.XpLedger.AsNoTracking()
            .Where(entry => userIds.Contains(entry.UserId) &&
                            entry.LocalActivityDate >= weekStart &&
                            entry.LocalActivityDate <= weekEnd)
            .GroupBy(entry => entry.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                WeeklyXp = group.Sum(item => item.Amount),
                LastActivityAtUtc = group.Max(item => item.CreatedAtUtc)
            })
            .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var profilesByUserId = await db.UserLeagueProfiles
            .Where(profile => userIds.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, cancellationToken);

        foreach (var standing in standings)
        {
            if (suspendedUserIds.Contains(standing.UserId) || hiddenUserIds.Contains(standing.UserId))
            {
                standing.FinalWeeklyXp = xpByUserId.TryGetValue(standing.UserId, out var hiddenXp) ? hiddenXp.WeeklyXp : 0;
                standing.FinalRank = null;
                standing.Outcome = LeagueWeekOutcome.Stayed;
                standing.SettledAtUtc = nowUtc;
            }
        }

        var activeStandings = standings
            .Where(item => !suspendedUserIds.Contains(item.UserId) && !hiddenUserIds.Contains(item.UserId))
            .ToList();

        foreach (var tierGroup in activeStandings.GroupBy(item => item.Tier))
        {
            var tier = tierGroup.Key;
            var ordered = tierGroup
                .OrderByDescending(item => xpByUserId.TryGetValue(item.UserId, out var row) ? row.WeeklyXp : 0)
                .ThenBy(item => xpByUserId.TryGetValue(item.UserId, out var row) ? row.LastActivityAtUtc : DateTimeOffset.MaxValue)
                .ThenBy(item => item.UserId)
                .ToList();

            var totalInTier = ordered.Count;
            var demotionCutoffRank = Math.Max(0, totalInTier - SocialDefaults.LeagueDemotionSlots);

            for (var index = 0; index < ordered.Count; index++)
            {
                var standing = ordered[index];
                var rank = index + 1;
                var weeklyXp = xpByUserId.TryGetValue(standing.UserId, out var xpRow) ? xpRow.WeeklyXp : 0;

                var isTopProtectedOrPromoted = rank <= SocialDefaults.LeaguePromotionSlots && weeklyXp > 0;
                LeagueWeekOutcome outcome;
                LeagueTier nextTier = tier;

                if (isTopProtectedOrPromoted && tier < LeagueTier.Gold)
                {
                    outcome = LeagueWeekOutcome.Promoted;
                    nextTier = tier + 1;
                }
                else if (!isTopProtectedOrPromoted && tier > LeagueTier.Bronze && rank > demotionCutoffRank)
                {
                    outcome = LeagueWeekOutcome.Demoted;
                    nextTier = tier - 1;
                }
                else
                {
                    outcome = LeagueWeekOutcome.Stayed;
                }

                standing.FinalWeeklyXp = weeklyXp;
                standing.FinalRank = rank;
                standing.Outcome = outcome;
                standing.SettledAtUtc = nowUtc;

                if (!profilesByUserId.TryGetValue(standing.UserId, out var profile))
                {
                    profile = new UserLeagueProfile
                    {
                        UserId = standing.UserId,
                        CurrentTier = nextTier,
                        LastSettledWeekStartLocal = weekStart,
                        LastOutcome = outcome,
                        UpdatedAtUtc = nowUtc
                    };
                    db.UserLeagueProfiles.Add(profile);
                    profilesByUserId[standing.UserId] = profile;
                }
                else
                {
                    profile.CurrentTier = nextTier;
                    profile.LastSettledWeekStartLocal = weekStart;
                    profile.LastOutcome = outcome;
                    profile.UpdatedAtUtc = nowUtc;
                }
            }
        }
    }

    public static string DetermineLiveZone(LeagueTier tier, int rank, int weeklyXp, int totalParticipantsInTier)
    {
        var isTopTenWithXp = rank <= SocialDefaults.LeaguePromotionSlots && weeklyXp > 0;
        if (isTopTenWithXp && tier < LeagueTier.Gold)
            return "promotion";

        var demotionCutoff = Math.Max(0, totalParticipantsInTier - SocialDefaults.LeagueDemotionSlots);
        if (!isTopTenWithXp && tier > LeagueTier.Bronze && rank > demotionCutoff)
            return "demotion";

        return "safe";
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
