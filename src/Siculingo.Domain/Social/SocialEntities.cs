namespace Siculingo.Domain.Social;

public enum ProfileVisibility
{
    Public = 0,
    FriendsOnly = 1,
    Private = 2
}

public sealed class UserPrivacySettings
{
    public Guid UserId { get; set; }
    public ProfileVisibility ProfileVisibility { get; set; } = ProfileVisibility.Public;
    public bool AllowFriendRequests { get; set; } = true;
    public bool ShowInLeaderboard { get; set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1
}

public sealed class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserLowId { get; set; }
    public Guid UserHighId { get; set; }
    public Guid RequesterUserId { get; set; }
    public Guid AddresseeUserId { get; set; }
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
}

public sealed class UserBlock
{
    public Guid BlockerUserId { get; set; }
    public Guid BlockedUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum LeagueTier
{
    Bronze = 1,
    Silver = 2,
    Gold = 3
}

public enum LeagueWeekOutcome
{
    InProgress = 0,
    Stayed = 1,
    Promoted = 2,
    Demoted = 3
}

public sealed class UserLeagueProfile
{
    public Guid UserId { get; set; }
    public LeagueTier CurrentTier { get; set; } = LeagueTier.Bronze;
    public DateOnly? LastSettledWeekStartLocal { get; set; }
    public LeagueWeekOutcome LastOutcome { get; set; } = LeagueWeekOutcome.InProgress;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class WeeklyLeagueStanding
{
    public DateOnly WeekStartLocal { get; set; }
    public Guid UserId { get; set; }
    public LeagueTier Tier { get; set; } = LeagueTier.Bronze;
    public int? FinalWeeklyXp { get; set; }
    public int? FinalRank { get; set; }
    public LeagueWeekOutcome Outcome { get; set; } = LeagueWeekOutcome.InProgress;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SettledAtUtc { get; set; }
}

public static class SocialDefaults
{
    public const int LeaderboardTopLimit = 50;
    public const int LeaguePromotionSlots = 10;
    public const int LeagueDemotionSlots = 5;

    public static (Guid LowId, Guid HighId) OrderUserPair(Guid first, Guid second)
    {
        var comparison = string.CompareOrdinal(first.ToString("D"), second.ToString("D"));
        return comparison <= 0 ? (first, second) : (second, first);
    }
}
