using Microsoft.EntityFrameworkCore;
using Siculingo.Domain.Gamification;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Gamification;

public sealed class HeartsOptions
{
    public const string SectionName = "Hearts";
    public bool ConsumptionEnabled { get; set; } = true;
}

public sealed record HeartsStatusDto(
    int CurrentHearts,
    int MaxHearts,
    bool ConsumptionEnabled,
    DateTimeOffset? NextHeartAtUtc,
    int? SecondsUntilNextHeart,
    bool CanStartPathLesson,
    bool PracticeRestoresHeart);

public static class HeartsService
{
    private static readonly TimeSpan RegenerationStep = TimeSpan.FromHours(GamificationDefaults.HeartRegenerationHours);

    public static (int EffectiveHearts, DateTimeOffset? EffectiveAnchorUtc) CalculateEffectiveState(
        UserHearts? stored,
        DateTimeOffset nowUtc)
    {
        if (stored is null)
            return (GamificationDefaults.MaxHearts, null);

        var current = Math.Clamp(stored.CurrentHearts, 0, GamificationDefaults.MaxHearts);
        if (current >= GamificationDefaults.MaxHearts)
            return (GamificationDefaults.MaxHearts, null);

        var anchor = stored.RegenerationAnchorUtc ?? nowUtc;
        if (nowUtc <= anchor)
            return (current, anchor);

        var elapsed = nowUtc - anchor;
        var steps = (int)Math.Floor(elapsed.TotalHours / GamificationDefaults.HeartRegenerationHours);
        if (steps <= 0)
            return (current, anchor);

        var regenerated = Math.Min(GamificationDefaults.MaxHearts, current + steps);
        if (regenerated >= GamificationDefaults.MaxHearts)
            return (GamificationDefaults.MaxHearts, null);

        return (regenerated, anchor.Add(TimeSpan.FromHours(steps * GamificationDefaults.HeartRegenerationHours)));
    }

    public static HeartsStatusDto BuildStatusDto(
        int effectiveHearts,
        DateTimeOffset? effectiveAnchorUtc,
        DateTimeOffset nowUtc,
        bool consumptionEnabled)
    {
        DateTimeOffset? nextHeartAtUtc = null;
        int? secondsUntilNext = null;

        if (effectiveHearts < GamificationDefaults.MaxHearts && effectiveAnchorUtc.HasValue)
        {
            nextHeartAtUtc = effectiveAnchorUtc.Value.Add(RegenerationStep);
            var remaining = nextHeartAtUtc.Value - nowUtc;
            secondsUntilNext = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        }

        var canStartPath = !consumptionEnabled || effectiveHearts > 0;
        return new HeartsStatusDto(
            effectiveHearts,
            GamificationDefaults.MaxHearts,
            consumptionEnabled,
            nextHeartAtUtc,
            secondsUntilNext,
            canStartPath,
            PracticeRestoresHeart: true);
    }

    public static async Task<HeartsStatusDto> GetStatusSnapshotAsync(
        SiculingoDbContext db,
        Guid userId,
        DateTimeOffset nowUtc,
        bool consumptionEnabled,
        CancellationToken cancellationToken)
    {
        var stored = await db.UserHearts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var (effectiveHearts, effectiveAnchor) = CalculateEffectiveState(stored, nowUtc);
        return BuildStatusDto(effectiveHearts, effectiveAnchor, nowUtc, consumptionEnabled);
    }

    public static async Task<UserHearts> GetOrCreateNormalizedTrackedAsync(
        SiculingoDbContext db,
        Guid userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var hearts = await db.UserHearts.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (hearts is null)
        {
            hearts = new UserHearts
            {
                UserId = userId,
                CurrentHearts = GamificationDefaults.MaxHearts,
                RegenerationAnchorUtc = null,
                UpdatedAtUtc = nowUtc
            };
            db.UserHearts.Add(hearts);
            return hearts;
        }

        var (effectiveHearts, effectiveAnchor) = CalculateEffectiveState(hearts, nowUtc);
        if (hearts.CurrentHearts != effectiveHearts || hearts.RegenerationAnchorUtc != effectiveAnchor)
        {
            hearts.CurrentHearts = effectiveHearts;
            hearts.RegenerationAnchorUtc = effectiveAnchor;
            hearts.UpdatedAtUtc = nowUtc;
        }

        return hearts;
    }

    public static async Task<HeartsStatusDto> ConsumeHeartOnWrongPathAnswerAsync(
        SiculingoDbContext db,
        Guid userId,
        DateTimeOffset nowUtc,
        bool consumptionEnabled,
        CancellationToken cancellationToken)
    {
        var hearts = await GetOrCreateNormalizedTrackedAsync(db, userId, nowUtc, cancellationToken);
        if (consumptionEnabled && hearts.CurrentHearts > 0)
        {
            if (hearts.CurrentHearts >= GamificationDefaults.MaxHearts || hearts.RegenerationAnchorUtc is null)
                hearts.RegenerationAnchorUtc = nowUtc;

            hearts.CurrentHearts = Math.Max(0, hearts.CurrentHearts - 1);
            hearts.UpdatedAtUtc = nowUtc;
        }

        return BuildStatusDto(hearts.CurrentHearts, hearts.RegenerationAnchorUtc, nowUtc, consumptionEnabled);
    }

    public static async Task<(bool Restored, HeartsStatusDto Status)> RestoreHeartOnPracticeCompletionAsync(
        SiculingoDbContext db,
        Guid userId,
        DateTimeOffset nowUtc,
        bool consumptionEnabled,
        CancellationToken cancellationToken)
    {
        var hearts = await GetOrCreateNormalizedTrackedAsync(db, userId, nowUtc, cancellationToken);
        var restored = false;
        if (hearts.CurrentHearts < GamificationDefaults.MaxHearts)
        {
            hearts.CurrentHearts += 1;
            if (hearts.CurrentHearts >= GamificationDefaults.MaxHearts)
                hearts.RegenerationAnchorUtc = null;
            hearts.UpdatedAtUtc = nowUtc;
            restored = true;
        }

        return (restored, BuildStatusDto(hearts.CurrentHearts, hearts.RegenerationAnchorUtc, nowUtc, consumptionEnabled));
    }
}
