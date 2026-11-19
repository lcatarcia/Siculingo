using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Gamification;
using Siculingo.Domain.Gamification;
using Siculingo.Domain.Social;
using Siculingo.Infrastructure.Accounts;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Social;

public static class SocialEndpoints
{
    public static void MapSocial(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/social")
            .WithTags("Social")
            .RequireAuthorization()
            .AddEndpointFilter<AccountAntiforgeryFilter>();

        group.MapGet("/privacy", GetPrivacy)
            .WithName("GetSocialPrivacySettings")
            .ProducesProblem(401);

        group.MapPost("/privacy", UpdatePrivacy)
            .WithName("UpdateSocialPrivacySettings")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

        group.MapGet("/overview", GetSocialOverview)
            .WithName("GetSocialOverview")
            .ProducesProblem(401);

        group.MapGet("/users/search", SearchUsers)
            .WithName("SearchSocialUsers")
            .ProducesProblem(400).ProducesProblem(401);

        group.MapGet("/profiles/{username}", GetPublicProfile)
            .WithName("GetPublicProfile")
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        group.MapPost("/friends/requests", SendFriendRequest)
            .WithName("SendFriendRequest")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/friends/requests/{friendshipId:guid}/accept", AcceptFriendRequest)
            .WithName("AcceptFriendRequest")
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/friends/requests/{friendshipId:guid}/reject", RejectOrCancelFriendRequest)
            .WithName("RejectFriendRequest")
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/friends/{friendUserId:guid}/remove", RemoveFriend)
            .WithName("RemoveFriend")
            .ProducesProblem(401).ProducesProblem(403);

        group.MapPost("/blocks", BlockUser)
            .WithName("BlockSocialUser")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        group.MapPost("/blocks/{blockedUserId:guid}/unblock", UnblockUser)
            .WithName("UnblockSocialUser")
            .ProducesProblem(401).ProducesProblem(403);

        group.MapGet("/leaderboard", GetLeaderboard)
            .WithName("GetWeeklyLeaderboard")
            .ProducesProblem(400).ProducesProblem(401);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 2601 or 2627 })
                return true;
        }
        return false;
    }

    private static string FormatVisibility(ProfileVisibility visibility) => visibility switch
    {
        ProfileVisibility.Public => "public",
        ProfileVisibility.FriendsOnly => "friends",
        ProfileVisibility.Private => "private",
        _ => "public"
    };

    private static bool TryParseVisibility(string? raw, out ProfileVisibility visibility)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "public":
                visibility = ProfileVisibility.Public;
                return true;
            case "friends":
                visibility = ProfileVisibility.FriendsOnly;
                return true;
            case "private":
                visibility = ProfileVisibility.Private;
                return true;
            default:
                visibility = ProfileVisibility.Public;
                return false;
        }
    }

    private static string DisplayNameOf(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? "Utente"
            : user.DisplayName;

    private static async Task<Results<Ok<PrivacySettingsResponse>, UnauthorizedHttpResult>> GetPrivacy(
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var settings = await db.UserPrivacySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        return TypedResults.Ok(new PrivacySettingsResponse(
            FormatVisibility(settings?.ProfileVisibility ?? ProfileVisibility.Public),
            settings?.AllowFriendRequests ?? true,
            settings?.ShowInLeaderboard ?? true));
    }

    private static async Task<Results<Ok<PrivacySettingsResponse>, UnauthorizedHttpResult, ProblemHttpResult>> UpdatePrivacy(
        UpdatePrivacySettingsRequest request,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        if (!TryParseVisibility(request.ProfileVisibility, out var visibility))
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Visibilità profilo non valida",
                detail: "Scegli una visibilità valida: public, friends oppure private.");
        }

        var nowUtc = clock.GetUtcNow();
        var allowRequests = visibility == ProfileVisibility.Private ? false : request.AllowFriendRequests;

        var settings = await db.UserPrivacySettings
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (settings is null)
        {
            settings = new UserPrivacySettings
            {
                UserId = userId,
                ProfileVisibility = visibility,
                AllowFriendRequests = allowRequests,
                ShowInLeaderboard = request.ShowInLeaderboard,
                UpdatedAtUtc = nowUtc
            };
            db.UserPrivacySettings.Add(settings);
        }
        else
        {
            settings.ProfileVisibility = visibility;
            settings.AllowFriendRequests = allowRequests;
            settings.ShowInLeaderboard = request.ShowInLeaderboard;
            settings.UpdatedAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (settings.ShowInLeaderboard)
        {
            var timeZone = GamificationCalculator.ResolveTimeZone(GamificationDefaults.DefaultTimeZoneId);
            var weekStart = GamificationCalculator.GetWeekStartMonday(GamificationCalculator.ToLocalDate(nowUtc, timeZone));
            await LeagueService.SettleCompletedWeeksAndEnsureCurrentAsync(db, userId, weekStart, nowUtc, cancellationToken);
        }

        return TypedResults.Ok(new PrivacySettingsResponse(
            FormatVisibility(settings.ProfileVisibility),
            settings.AllowFriendRequests,
            settings.ShowInLeaderboard));
    }

    private static async Task<Results<Ok<SocialOverviewResponse>, UnauthorizedHttpResult>> GetSocialOverview(
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var nowUtc = clock.GetUtcNow();
        var timeZone = GamificationCalculator.ResolveTimeZone(GamificationDefaults.DefaultTimeZoneId);
        var todayLocal = GamificationCalculator.ToLocalDate(nowUtc, timeZone);
        var weekStart = GamificationCalculator.GetWeekStartMonday(todayLocal);
        var weekEnd = weekStart.AddDays(6);

        var friendships = await db.Friendships.AsNoTracking()
            .Where(item => item.UserLowId == userId || item.UserHighId == userId)
            .OrderByDescending(item => item.AcceptedAtUtc ?? item.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var outgoingBlocks = await db.UserBlocks.AsNoTracking()
            .Where(item => item.BlockerUserId == userId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var relatedUserIds = friendships
            .Select(item => item.UserLowId == userId ? item.UserHighId : item.UserLowId)
            .Concat(outgoingBlocks.Select(item => item.BlockedUserId))
            .Distinct()
            .ToList();

        var usersById = relatedUserIds.Count == 0
            ? new Dictionary<Guid, ApplicationUser>()
            : await db.Users.AsNoTracking()
                .Where(user => relatedUserIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, cancellationToken);

        var statsById = relatedUserIds.Count == 0
            ? new Dictionary<Guid, UserStats>()
            : await db.UserStats.AsNoTracking()
                .Where(item => relatedUserIds.Contains(item.UserId))
                .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var streaksById = relatedUserIds.Count == 0
            ? new Dictionary<Guid, UserStreak>()
            : await db.UserStreaks.AsNoTracking()
                .Where(item => relatedUserIds.Contains(item.UserId))
                .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var privacyById = relatedUserIds.Count == 0
            ? new Dictionary<Guid, UserPrivacySettings>()
            : await db.UserPrivacySettings.AsNoTracking()
                .Where(item => relatedUserIds.Contains(item.UserId))
                .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var leagueById = relatedUserIds.Count == 0
            ? new Dictionary<Guid, UserLeagueProfile>()
            : await db.UserLeagueProfiles.AsNoTracking()
                .Where(item => relatedUserIds.Contains(item.UserId))
                .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var thresholds = await db.LevelThresholds.AsNoTracking()
            .OrderBy(item => item.Level)
            .ToListAsync(cancellationToken);

        var weeklyXpById = relatedUserIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.XpLedger.AsNoTracking()
                .Where(entry => relatedUserIds.Contains(entry.UserId) &&
                                entry.LocalActivityDate >= weekStart &&
                                entry.LocalActivityDate <= weekEnd)
                .GroupBy(entry => entry.UserId)
                .Select(group => new { UserId = group.Key, Sum = group.Sum(item => item.Amount) })
                .ToDictionaryAsync(item => item.UserId, item => item.Sum, cancellationToken);

        var acceptedFriends = new List<FriendSummaryDto>();
        var incomingRequests = new List<FriendRequestItemDto>();
        var outgoingRequests = new List<FriendRequestItemDto>();

        foreach (var friendship in friendships)
        {
            var otherId = friendship.UserLowId == userId ? friendship.UserHighId : friendship.UserLowId;
            if (!usersById.TryGetValue(otherId, out var otherUser) || otherUser.IsSuspended)
                continue;

            var username = otherUser.UserName ?? "utente";
            var displayName = DisplayNameOf(otherUser);
            privacyById.TryGetValue(otherId, out var otherPrivacy);
            var visibility = otherPrivacy?.ProfileVisibility ?? ProfileVisibility.Public;

            if (friendship.Status == FriendshipStatus.Accepted)
            {
                statsById.TryGetValue(otherId, out var otherStats);
                streaksById.TryGetValue(otherId, out var otherStreak);
                leagueById.TryGetValue(otherId, out var otherLeague);

                var levelSnapshot = GamificationCalculator.CalculateLevelProgress(otherStats?.TotalXp ?? 0, thresholds);
                var effectiveStreak = GamificationCalculator.CalculateEffectiveCurrentStreak(otherStreak, todayLocal);
                var canViewProfile = visibility != ProfileVisibility.Private;
                var weeklyXp = (otherPrivacy?.ShowInLeaderboard ?? true) && weeklyXpById.TryGetValue(otherId, out var wxp)
                    ? wxp
                    : 0;

                acceptedFriends.Add(new FriendSummaryDto(
                    friendship.Id,
                    otherId,
                    username,
                    displayName,
                    levelSnapshot.CurrentLevel,
                    levelSnapshot.LevelTitle,
                    canViewProfile ? (otherStats?.TotalXp ?? 0) : 0,
                    weeklyXp,
                    canViewProfile ? effectiveStreak : 0,
                    LeagueService.FormatTierCode(otherLeague?.CurrentTier ?? LeagueTier.Bronze),
                    LeagueService.FormatTierName(otherLeague?.CurrentTier ?? LeagueTier.Bronze),
                    canViewProfile,
                    friendship.AcceptedAtUtc ?? friendship.CreatedAtUtc));
            }
            else if (friendship.AddresseeUserId == userId)
            {
                incomingRequests.Add(new FriendRequestItemDto(
                    friendship.Id,
                    otherId,
                    username,
                    displayName,
                    "incoming",
                    visibility == ProfileVisibility.Public,
                    friendship.CreatedAtUtc));
            }
            else
            {
                outgoingRequests.Add(new FriendRequestItemDto(
                    friendship.Id,
                    otherId,
                    username,
                    displayName,
                    "outgoing",
                    visibility == ProfileVisibility.Public,
                    friendship.CreatedAtUtc));
            }
        }

        var blockedDtos = outgoingBlocks
            .Where(item => usersById.ContainsKey(item.BlockedUserId))
            .Select(item =>
            {
                var blocked = usersById[item.BlockedUserId];
                return new BlockedUserDto(
                    blocked.Id,
                    blocked.UserName ?? "utente",
                    DisplayNameOf(blocked),
                    item.CreatedAtUtc);
            })
            .ToList();

        return TypedResults.Ok(new SocialOverviewResponse(
            acceptedFriends,
            incomingRequests,
            outgoingRequests,
            blockedDtos));
    }

    private static async Task<Results<Ok<UserSearchResponse>, UnauthorizedHttpResult, ProblemHttpResult>> SearchUsers(
        string? q,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var query = q?.Trim() ?? string.Empty;
        if (query.Length < 2 || query.Length > 40)
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Ricerca troppo corta",
                detail: "Inserisci almeno 2 caratteri (massimo 40) per cercare un utente.");
        }

        // Exclude blocked users in EITHER direction
        var blockedEitherDirection = (await db.UserBlocks.AsNoTracking()
            .Where(item => item.BlockerUserId == userId || item.BlockedUserId == userId)
            .Select(item => item.BlockerUserId == userId ? item.BlockedUserId : item.BlockerUserId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var normalizedUpper = query.ToUpperInvariant();
        var candidates = await db.Users.AsNoTracking()
            .Where(user => user.Id != userId &&
                           !user.IsSuspended &&
                           ((user.NormalizedUserName != null && user.NormalizedUserName.Contains(normalizedUpper)) ||
                            (user.DisplayName != null && user.DisplayName.Contains(query))))
            .OrderBy(user => user.UserName)
            .Take(40)
            .ToListAsync(cancellationToken);

        var candidateIds = candidates
            .Where(user => !blockedEitherDirection.Contains(user.Id))
            .Select(user => user.Id)
            .ToList();

        var privacyMap = candidateIds.Count == 0
            ? new Dictionary<Guid, UserPrivacySettings>()
            : await db.UserPrivacySettings.AsNoTracking()
                .Where(item => candidateIds.Contains(item.UserId))
                .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var friendships = candidateIds.Count == 0
            ? []
            : await db.Friendships.AsNoTracking()
                .Where(item => (item.UserLowId == userId && candidateIds.Contains(item.UserHighId)) ||
                               (item.UserHighId == userId && candidateIds.Contains(item.UserLowId)))
                .ToListAsync(cancellationToken);

        var friendshipByOtherId = friendships.ToDictionary(
            item => item.UserLowId == userId ? item.UserHighId : item.UserLowId);

        var statsMap = candidateIds.Count == 0
            ? new Dictionary<Guid, UserStats>()
            : await db.UserStats.AsNoTracking()
                .Where(item => candidateIds.Contains(item.UserId))
                .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var thresholds = await db.LevelThresholds.AsNoTracking()
            .OrderBy(item => item.Level)
            .ToListAsync(cancellationToken);

        var results = new List<UserSearchItemDto>();
        foreach (var candidate in candidates)
        {
            if (blockedEitherDirection.Contains(candidate.Id))
                continue;

            privacyMap.TryGetValue(candidate.Id, out var privacy);
            var visibility = privacy?.ProfileVisibility ?? ProfileVisibility.Public;
            if (visibility == ProfileVisibility.Private)
                continue;

            friendshipByOtherId.TryGetValue(candidate.Id, out var friendship);
            var isFriend = friendship?.Status == FriendshipStatus.Accepted;
            var relationship = friendship switch
            {
                null => "none",
                { Status: FriendshipStatus.Accepted } => "friends",
                { RequesterUserId: var reqId } when reqId == userId => "outgoing_pending",
                _ => "incoming_pending"
            };

            var allowRequests = privacy?.AllowFriendRequests ?? true;
            var canRequest = relationship == "none" && allowRequests || relationship == "incoming_pending";
            var canViewProfile = visibility == ProfileVisibility.Public || (visibility == ProfileVisibility.FriendsOnly && isFriend);

            statsMap.TryGetValue(candidate.Id, out var stats);
            var levelInfo = GamificationCalculator.CalculateLevelProgress(stats?.TotalXp ?? 0, thresholds);

            results.Add(new UserSearchItemDto(
                candidate.Id,
                candidate.UserName ?? "utente",
                DisplayNameOf(candidate),
                levelInfo.CurrentLevel,
                levelInfo.LevelTitle,
                relationship,
                friendship?.Id,
                canRequest,
                canViewProfile));

            if (results.Count >= 20)
                break;
        }

        return TypedResults.Ok(new UserSearchResponse(query, results));
    }

    private static async Task<Results<Ok<PublicProfileResponse>, UnauthorizedHttpResult, ProblemHttpResult>> GetPublicProfile(
        string username,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var viewerUserId))
            return TypedResults.Unauthorized();

        var normalized = username.Trim().ToUpperInvariant();
        var targetUser = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.NormalizedUserName == normalized && !user.IsSuspended, cancellationToken);

        if (targetUser is null)
        {
            return TypedResults.Problem(
                statusCode: 404,
                title: "Profilo non trovato",
                detail: "L'utente cercato non esiste o non è disponibile.");
        }

        var isSelf = targetUser.Id == viewerUserId;
        string relationshipStatus = isSelf ? "self" : "none";
        Guid? friendshipId = null;

        if (!isSelf)
        {
            var hasBlock = await db.UserBlocks.AsNoTracking()
                .AnyAsync(
                    block => (block.BlockerUserId == viewerUserId && block.BlockedUserId == targetUser.Id) ||
                             (block.BlockerUserId == targetUser.Id && block.BlockedUserId == viewerUserId),
                    cancellationToken);

            if (hasBlock)
            {
                return TypedResults.Problem(
                    statusCode: 403,
                    title: "Profilo non disponibile",
                    detail: "Non puoi visualizzare questo profilo.");
            }

            var (lowId, highId) = SocialDefaults.OrderUserPair(viewerUserId, targetUser.Id);
            var friendship = await db.Friendships.AsNoTracking()
                .SingleOrDefaultAsync(item => item.UserLowId == lowId && item.UserHighId == highId, cancellationToken);

            if (friendship is not null)
            {
                friendshipId = friendship.Id;
                relationshipStatus = friendship.Status == FriendshipStatus.Accepted
                    ? "friends"
                    : friendship.RequesterUserId == viewerUserId ? "outgoing_pending" : "incoming_pending";
            }
        }

        var privacy = await db.UserPrivacySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == targetUser.Id, cancellationToken);
        var visibility = privacy?.ProfileVisibility ?? ProfileVisibility.Public;

        if (!isSelf)
        {
            if (visibility == ProfileVisibility.Private)
            {
                return TypedResults.Problem(
                    statusCode: 403,
                    title: "Profilo privato",
                    detail: "Questo utente ha scelto di mantenere privato il proprio profilo.");
            }

            if (visibility == ProfileVisibility.FriendsOnly && relationshipStatus != "friends")
            {
                return TypedResults.Problem(
                    statusCode: 403,
                    title: "Profilo riservato agli amici",
                    detail: "Solo gli amici confermati possono visualizzare i dettagli di questo profilo.");
            }
        }

        var nowUtc = clock.GetUtcNow();
        var stats = await db.UserStats.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == targetUser.Id, cancellationToken);
        var streak = await db.UserStreaks.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == targetUser.Id, cancellationToken);
        var thresholds = await db.LevelThresholds.AsNoTracking()
            .OrderBy(item => item.Level)
            .ToListAsync(cancellationToken);
        var league = await db.UserLeagueProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == targetUser.Id, cancellationToken);

        var timeZone = GamificationCalculator.ResolveTimeZone(streak?.TimeZoneId);
        var todayLocal = GamificationCalculator.ToLocalDate(nowUtc, timeZone);
        var weekStart = GamificationCalculator.GetWeekStartMonday(todayLocal);
        var weekEnd = weekStart.AddDays(6);

        int? weeklyXp = null;
        if (isSelf || (privacy?.ShowInLeaderboard ?? true))
        {
            weeklyXp = await db.XpLedger.AsNoTracking()
                .Where(entry => entry.UserId == targetUser.Id &&
                                entry.LocalActivityDate >= weekStart &&
                                entry.LocalActivityDate <= weekEnd)
                .SumAsync(entry => (int?)entry.Amount, cancellationToken) ?? 0;
        }

        var unlockedTrophyRows = await db.UserAchievements.AsNoTracking()
            .Where(item => item.UserId == targetUser.Id)
            .Join(
                db.Achievements.AsNoTracking(),
                ua => ua.AchievementId,
                def => def.Id,
                (ua, def) => new
                {
                    def.Code,
                    def.Name,
                    def.IconPath,
                    def.Rarity,
                    ua.UnlockedAtUtc
                })
            .OrderByDescending(item => item.UnlockedAtUtc)
            .ToListAsync(cancellationToken);

        var unlockedTrophies = unlockedTrophyRows
            .Select(row => new PublicAchievementBadgeDto(
                row.Code,
                row.Name,
                row.IconPath,
                GamificationCalculator.FormatRarity(row.Rarity),
                row.UnlockedAtUtc))
            .ToList();

        var levelProgress = GamificationCalculator.CalculateLevelProgress(stats?.TotalXp ?? 0, thresholds);
        var effectiveStreak = GamificationCalculator.CalculateEffectiveCurrentStreak(streak, todayLocal);
        var tier = league?.CurrentTier ?? LeagueTier.Bronze;
        var canRequest = !isSelf &&
            (relationshipStatus == "incoming_pending" ||
             (relationshipStatus == "none" && (privacy?.AllowFriendRequests ?? true)));

        return TypedResults.Ok(new PublicProfileResponse(
            targetUser.Id,
            targetUser.UserName ?? "utente",
            DisplayNameOf(targetUser),
            targetUser.RegisteredAtUtc,
            levelProgress.CurrentLevel,
            levelProgress.LevelTitle,
            levelProgress.TotalXp,
            weeklyXp,
            effectiveStreak,
            streak?.BestStreak ?? 0,
            stats?.UniqueLessonsCompleted ?? 0,
            LeagueService.FormatTierCode(tier),
            LeagueService.FormatTierName(tier),
            unlockedTrophies.Count,
            unlockedTrophies.Take(6).ToList(),
            relationshipStatus,
            friendshipId,
            canRequest));
    }

    private static async Task<Results<Ok<FriendActionResponse>, UnauthorizedHttpResult, ProblemHttpResult>> SendFriendRequest(
        SendFriendRequestPayload request,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        if (request.TargetUserId == Guid.Empty || request.TargetUserId == userId)
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Operazione non valida",
                detail: "Non puoi inviare una richiesta di amicizia a te stesso.");
        }

        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var nowUtc = clock.GetUtcNow();

                var targetUser = await db.Users.AsNoTracking()
                    .SingleOrDefaultAsync(user => user.Id == request.TargetUserId && !user.IsSuspended, cancellationToken);
                if (targetUser is null)
                {
                    return TypedResults.Problem(
                        statusCode: 404,
                        title: "Utente non trovato",
                        detail: "L'account indicato non esiste o non è attivo.");
                }

                var isBlocked = await db.UserBlocks
                    .AnyAsync(
                        block => (block.BlockerUserId == userId && block.BlockedUserId == request.TargetUserId) ||
                                 (block.BlockerUserId == request.TargetUserId && block.BlockedUserId == userId),
                        cancellationToken);
                if (isBlocked)
                {
                    return TypedResults.Problem(
                        statusCode: 403,
                        title: "Interazione non consentita",
                        detail: "Non è possibile inviare una richiesta di amicizia a questo utente.");
                }

                var (lowId, highId) = SocialDefaults.OrderUserPair(userId, request.TargetUserId);
                var existing = await db.Friendships
                    .SingleOrDefaultAsync(item => item.UserLowId == lowId && item.UserHighId == highId, cancellationToken);

                if (existing is not null)
                {
                    if (existing.Status == FriendshipStatus.Accepted)
                    {
                        await tx.CommitAsync(cancellationToken);
                        return TypedResults.Ok(new FriendActionResponse(
                            existing.Id,
                            request.TargetUserId,
                            "friends",
                            AutoAccepted: false));
                    }

                    if (existing.RequesterUserId == userId)
                    {
                        await tx.CommitAsync(cancellationToken);
                        return TypedResults.Ok(new FriendActionResponse(
                            existing.Id,
                            request.TargetUserId,
                            "outgoing_pending",
                            AutoAccepted: false));
                    }

                    // Reciprocal pending request: auto-accept atomically!
                    existing.Status = FriendshipStatus.Accepted;
                    existing.AcceptedAtUtc = nowUtc;
                    await db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    return TypedResults.Ok(new FriendActionResponse(
                        existing.Id,
                        request.TargetUserId,
                        "friends",
                        AutoAccepted: true));
                }

                var targetPrivacy = await db.UserPrivacySettings.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.UserId == request.TargetUserId, cancellationToken);
                if (targetPrivacy is not null &&
                    (!targetPrivacy.AllowFriendRequests || targetPrivacy.ProfileVisibility == ProfileVisibility.Private))
                {
                    return TypedResults.Problem(
                        statusCode: 403,
                        title: "Richieste disattivate",
                        detail: "Questo utente non accetta nuove richieste di amicizia.");
                }

                var created = new Friendship
                {
                    UserLowId = lowId,
                    UserHighId = highId,
                    RequesterUserId = userId,
                    AddresseeUserId = request.TargetUserId,
                    Status = FriendshipStatus.Pending,
                    CreatedAtUtc = nowUtc
                };
                db.Friendships.Add(created);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return TypedResults.Ok(new FriendActionResponse(
                    created.Id,
                    request.TargetUserId,
                    "outgoing_pending",
                    AutoAccepted: false));
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception) && retry < 2)
            {
                db.ChangeTracker.Clear();
            }
        }

        return TypedResults.Problem(
            statusCode: 409,
            title: "Richiesta già elaborata",
            detail: "Aggiorna l'elenco amici e riprova.");
    }

    private static async Task<Results<Ok<FriendActionResponse>, UnauthorizedHttpResult, ProblemHttpResult>> AcceptFriendRequest(
        Guid friendshipId,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var friendship = await db.Friendships
                    .SingleOrDefaultAsync(item => item.Id == friendshipId, cancellationToken);

                if (friendship is null)
                {
                    return TypedResults.Problem(
                        statusCode: 404,
                        title: "Richiesta non trovata",
                        detail: "Questa richiesta di amicizia non esiste più.");
                }

                var otherUserId = friendship.UserLowId == userId ? friendship.UserHighId : friendship.UserLowId;
                if (friendship.UserLowId != userId && friendship.UserHighId != userId)
                {
                    return TypedResults.Problem(
                        statusCode: 404,
                        title: "Richiesta non trovata",
                        detail: "Questa richiesta di amicizia non appartiene al tuo account.");
                }

                if (friendship.Status == FriendshipStatus.Accepted)
                {
                    await tx.CommitAsync(cancellationToken);
                    return TypedResults.Ok(new FriendActionResponse(
                        friendship.Id,
                        otherUserId,
                        "friends",
                        AutoAccepted: false));
                }

                if (friendship.AddresseeUserId != userId)
                {
                    return TypedResults.Problem(
                        statusCode: 403,
                        title: "Operazione non autorizzata",
                        detail: "Solo il destinatario può accettare una richiesta di amicizia in entrata.");
                }

                var isBlocked = await db.UserBlocks
                    .AnyAsync(
                        block => (block.BlockerUserId == userId && block.BlockedUserId == otherUserId) ||
                                 (block.BlockerUserId == otherUserId && block.BlockedUserId == userId),
                        cancellationToken);
                if (isBlocked)
                {
                    db.Friendships.Remove(friendship);
                    await db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    return TypedResults.Problem(
                        statusCode: 403,
                        title: "Interazione bloccata",
                        detail: "Non è possibile accettare questa richiesta.");
                }

                friendship.Status = FriendshipStatus.Accepted;
                friendship.AcceptedAtUtc = clock.GetUtcNow();
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return TypedResults.Ok(new FriendActionResponse(
                    friendship.Id,
                    otherUserId,
                    "friends",
                    AutoAccepted: false));
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception) && retry < 2)
            {
                db.ChangeTracker.Clear();
            }
        }

        return TypedResults.Problem(
            statusCode: 409,
            title: "Accettazione in corso",
            detail: "Ricarica l'elenco richieste.");
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RejectOrCancelFriendRequest(
        Guid friendshipId,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var friendship = await db.Friendships
            .SingleOrDefaultAsync(item => item.Id == friendshipId, cancellationToken);

        if (friendship is null)
        {
            await tx.CommitAsync(cancellationToken);
            return TypedResults.NoContent();
        }

        if (friendship.UserLowId != userId && friendship.UserHighId != userId)
        {
            return TypedResults.Problem(
                statusCode: 404,
                title: "Richiesta non trovata",
                detail: "Questa richiesta di amicizia non appartiene al tuo account.");
        }

        if (friendship.Status == FriendshipStatus.Pending)
        {
            db.Friendships.Remove(friendship);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> RemoveFriend(
        Guid friendUserId,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        if (friendUserId == Guid.Empty || friendUserId == userId)
            return TypedResults.NoContent();

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var (lowId, highId) = SocialDefaults.OrderUserPair(userId, friendUserId);
        var friendship = await db.Friendships
            .SingleOrDefaultAsync(item => item.UserLowId == lowId && item.UserHighId == highId, cancellationToken);

        if (friendship is not null)
        {
            db.Friendships.Remove(friendship);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> BlockUser(
        BlockUserRequestPayload request,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        if (request.TargetUserId == Guid.Empty || request.TargetUserId == userId)
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Blocco non valido",
                detail: "Non puoi bloccare il tuo stesso account.");
        }

        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var targetExists = await db.Users.AsNoTracking()
                    .AnyAsync(user => user.Id == request.TargetUserId, cancellationToken);
                if (!targetExists)
                {
                    return TypedResults.Problem(
                        statusCode: 404,
                        title: "Utente non trovato",
                        detail: "L'utente selezionato non esiste.");
                }

                var existingBlock = await db.UserBlocks
                    .SingleOrDefaultAsync(
                        item => item.BlockerUserId == userId && item.BlockedUserId == request.TargetUserId,
                        cancellationToken);

                if (existingBlock is null)
                {
                    db.UserBlocks.Add(new UserBlock
                    {
                        BlockerUserId = userId,
                        BlockedUserId = request.TargetUserId,
                        CreatedAtUtc = clock.GetUtcNow()
                    });
                }

                var (lowId, highId) = SocialDefaults.OrderUserPair(userId, request.TargetUserId);
                var existingFriendship = await db.Friendships
                    .SingleOrDefaultAsync(item => item.UserLowId == lowId && item.UserHighId == highId, cancellationToken);
                if (existingFriendship is not null)
                {
                    db.Friendships.Remove(existingFriendship);
                }

                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return TypedResults.NoContent();
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception) && retry < 2)
            {
                db.ChangeTracker.Clear();
            }
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> UnblockUser(
        Guid blockedUserId,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var existingBlock = await db.UserBlocks
            .SingleOrDefaultAsync(
                item => item.BlockerUserId == userId && item.BlockedUserId == blockedUserId,
                cancellationToken);

        if (existingBlock is not null)
        {
            db.UserBlocks.Remove(existingBlock);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<LeaderboardResponse>, UnauthorizedHttpResult, ProblemHttpResult>> GetLeaderboard(
        string? scope,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "league" : scope.Trim().ToLowerInvariant();
        if (normalizedScope is not ("league" or "friends"))
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Filtro classifica non valido",
                detail: "Usa scope=league oppure scope=friends.");
        }

        var nowUtc = clock.GetUtcNow();
        var timeZone = GamificationCalculator.ResolveTimeZone(GamificationDefaults.DefaultTimeZoneId);
        var todayLocal = GamificationCalculator.ToLocalDate(nowUtc, timeZone);
        var weekStart = GamificationCalculator.GetWeekStartMonday(todayLocal);
        var weekEnd = weekStart.AddDays(6);

        await LeagueService.SettleCompletedWeeksAndEnsureCurrentAsync(db, userId, weekStart, nowUtc, cancellationToken);

        var viewerPrivacy = await db.UserPrivacySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var viewerParticipating = viewerPrivacy?.ShowInLeaderboard ?? true;

        var viewerLeague = await db.UserLeagueProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var currentTier = viewerLeague?.CurrentTier ?? LeagueTier.Bronze;

        var blockedEitherDirection = (await db.UserBlocks.AsNoTracking()
            .Where(item => item.BlockerUserId == userId || item.BlockedUserId == userId)
            .Select(item => item.BlockerUserId == userId ? item.BlockedUserId : item.BlockerUserId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var acceptedFriendIds = (await db.Friendships.AsNoTracking()
            .Where(item => item.Status == FriendshipStatus.Accepted &&
                           (item.UserLowId == userId || item.UserHighId == userId))
            .Select(item => item.UserLowId == userId ? item.UserHighId : item.UserLowId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var thresholds = await db.LevelThresholds.AsNoTracking()
            .OrderBy(item => item.Level)
            .ToListAsync(cancellationToken);

        List<Guid> participantIds;
        if (normalizedScope == "friends")
        {
            var rawFriendScopeIds = acceptedFriendIds.Append(userId).Distinct().ToList();
            var hiddenIds = (await db.UserPrivacySettings.AsNoTracking()
                .Where(item => rawFriendScopeIds.Contains(item.UserId) && !item.ShowInLeaderboard)
                .Select(item => item.UserId)
                .ToListAsync(cancellationToken)).ToHashSet();

            var activeIds = (await db.Users.AsNoTracking()
                .Where(user => rawFriendScopeIds.Contains(user.Id) && !user.IsSuspended)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken)).ToHashSet();

            participantIds = rawFriendScopeIds
                .Where(id => activeIds.Contains(id) &&
                             !blockedEitherDirection.Contains(id) &&
                             (id == userId || !hiddenIds.Contains(id)))
                .ToList();
        }
        else
        {
            var standingUserIds = await db.WeeklyLeagueStandings.AsNoTracking()
                .Where(item => item.WeekStartLocal == weekStart && item.Tier == currentTier)
                .Select(item => item.UserId)
                .ToListAsync(cancellationToken);

            var hiddenIds = (await db.UserPrivacySettings.AsNoTracking()
                .Where(item => standingUserIds.Contains(item.UserId) && !item.ShowInLeaderboard)
                .Select(item => item.UserId)
                .ToListAsync(cancellationToken)).ToHashSet();

            var activeIds = (await db.Users.AsNoTracking()
                .Where(user => standingUserIds.Contains(user.Id) && !user.IsSuspended)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken)).ToHashSet();

            participantIds = standingUserIds
                .Where(id => activeIds.Contains(id) && !hiddenIds.Contains(id))
                .ToList();
        }

        var xpByUser = participantIds.Count == 0
            ? new Dictionary<Guid, (int WeeklyXp, DateTimeOffset LastAtUtc)>()
            : await db.XpLedger.AsNoTracking()
                .Where(entry => participantIds.Contains(entry.UserId) &&
                                entry.LocalActivityDate >= weekStart &&
                                entry.LocalActivityDate <= weekEnd)
                .GroupBy(entry => entry.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    WeeklyXp = group.Sum(item => item.Amount),
                    LastAtUtc = group.Max(item => item.CreatedAtUtc)
                })
                .ToDictionaryAsync(item => item.UserId, item => (item.WeeklyXp, item.LastAtUtc), cancellationToken);

        var usersMap = participantIds.Count == 0
            ? new Dictionary<Guid, ApplicationUser>()
            : await db.Users.AsNoTracking()
                .Where(user => participantIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, cancellationToken);

        var statsMap = participantIds.Count == 0
            ? new Dictionary<Guid, UserStats>()
            : await db.UserStats.AsNoTracking()
                .Where(item => participantIds.Contains(item.UserId))
                .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var privacyMap = participantIds.Count == 0
            ? new Dictionary<Guid, UserPrivacySettings>()
            : await db.UserPrivacySettings.AsNoTracking()
                .Where(item => participantIds.Contains(item.UserId))
                .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var orderedUserIds = participantIds
            .Where(usersMap.ContainsKey)
            .OrderByDescending(id => xpByUser.TryGetValue(id, out var row) ? row.WeeklyXp : 0)
            .ThenBy(id => xpByUser.TryGetValue(id, out var row) ? row.LastAtUtc : DateTimeOffset.MaxValue)
            .ThenBy(id => id)
            .ToList();

        var totalCount = orderedUserIds.Count;
        var allEntries = new List<LeaderboardEntryDto>(totalCount);

        for (var index = 0; index < orderedUserIds.Count; index++)
        {
            var entryUserId = orderedUserIds[index];
            var rank = index + 1;
            var user = usersMap[entryUserId];
            var weeklyXp = xpByUser.TryGetValue(entryUserId, out var xpInfo) ? xpInfo.WeeklyXp : 0;
            statsMap.TryGetValue(entryUserId, out var stats);
            privacyMap.TryGetValue(entryUserId, out var privacy);

            var levelInfo = GamificationCalculator.CalculateLevelProgress(stats?.TotalXp ?? 0, thresholds);
            var visibility = privacy?.ProfileVisibility ?? ProfileVisibility.Public;
            var isSelf = entryUserId == userId;
            var isFriend = acceptedFriendIds.Contains(entryUserId);
            var isBlocked = blockedEitherDirection.Contains(entryUserId);

            var canViewProfile = !isBlocked &&
                (isSelf ||
                 visibility == ProfileVisibility.Public ||
                 (visibility == ProfileVisibility.FriendsOnly && isFriend));

            var zone = normalizedScope == "league"
                ? LeagueService.DetermineLiveZone(currentTier, rank, weeklyXp, totalCount)
                : "safe";

            allEntries.Add(new LeaderboardEntryDto(
                rank,
                entryUserId,
                user.UserName ?? "utente",
                DisplayNameOf(user),
                weeklyXp,
                levelInfo.CurrentLevel,
                levelInfo.LevelTitle,
                isSelf,
                isFriend,
                canViewProfile,
                zone));
        }

        var topEntries = allEntries.Take(SocialDefaults.LeaderboardTopLimit).ToList();
        var viewerEntry = allEntries.SingleOrDefault(item => item.IsCurrentUser);

        return TypedResults.Ok(new LeaderboardResponse(
            normalizedScope,
            weekStart.ToString("yyyy-MM-dd"),
            weekEnd.ToString("yyyy-MM-dd"),
            GamificationDefaults.DefaultTimeZoneId,
            LeagueService.FormatTierCode(currentTier),
            LeagueService.FormatTierName(currentTier),
            SocialDefaults.LeaguePromotionSlots,
            SocialDefaults.LeagueDemotionSlots,
            viewerParticipating,
            viewerLeague?.LastSettledWeekStartLocal?.ToString("yyyy-MM-dd"),
            LeagueService.FormatOutcomeCode(viewerLeague?.LastOutcome ?? LeagueWeekOutcome.InProgress),
            totalCount,
            topEntries,
            viewerEntry));
    }
}

public sealed record PrivacySettingsResponse(
    string ProfileVisibility,
    bool AllowFriendRequests,
    bool ShowInLeaderboard);

public sealed record UpdatePrivacySettingsRequest(
    string ProfileVisibility,
    bool AllowFriendRequests,
    bool ShowInLeaderboard);

public sealed record FriendSummaryDto(
    Guid FriendshipId,
    Guid UserId,
    string Username,
    string DisplayName,
    int CurrentLevel,
    string LevelTitle,
    int TotalXp,
    int WeeklyXp,
    int CurrentStreak,
    string LeagueTierCode,
    string LeagueTierName,
    bool CanViewProfile,
    DateTimeOffset FriendsSinceUtc);

public sealed record FriendRequestItemDto(
    Guid FriendshipId,
    Guid UserId,
    string Username,
    string DisplayName,
    string Direction,
    bool CanViewProfile,
    DateTimeOffset CreatedAtUtc);

public sealed record BlockedUserDto(
    Guid UserId,
    string Username,
    string DisplayName,
    DateTimeOffset BlockedAtUtc);

public sealed record SocialOverviewResponse(
    IReadOnlyList<FriendSummaryDto> Friends,
    IReadOnlyList<FriendRequestItemDto> IncomingRequests,
    IReadOnlyList<FriendRequestItemDto> OutgoingRequests,
    IReadOnlyList<BlockedUserDto> BlockedUsers);

public sealed record UserSearchItemDto(
    Guid UserId,
    string Username,
    string DisplayName,
    int CurrentLevel,
    string LevelTitle,
    string RelationshipStatus,
    Guid? FriendshipId,
    bool CanRequestFriendship,
    bool CanViewProfile);

public sealed record UserSearchResponse(
    string Query,
    IReadOnlyList<UserSearchItemDto> Users);

public sealed record PublicAchievementBadgeDto(
    string Code,
    string Name,
    string IconPath,
    string Rarity,
    DateTimeOffset UnlockedAtUtc);

public sealed record PublicProfileResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    DateTimeOffset RegisteredAtUtc,
    int CurrentLevel,
    string LevelTitle,
    int TotalXp,
    int? WeeklyXp,
    int CurrentStreak,
    int BestStreak,
    int UniqueLessonsCompleted,
    string LeagueTierCode,
    string LeagueTierName,
    int UnlockedAchievementsCount,
    IReadOnlyList<PublicAchievementBadgeDto> RecentAchievements,
    string RelationshipStatus,
    Guid? FriendshipId,
    bool CanRequestFriendship);

public sealed record SendFriendRequestPayload(Guid TargetUserId);
public sealed record BlockUserRequestPayload(Guid TargetUserId);

public sealed record FriendActionResponse(
    Guid FriendshipId,
    Guid TargetUserId,
    string RelationshipStatus,
    bool AutoAccepted);

public sealed record LeaderboardEntryDto(
    int Rank,
    Guid UserId,
    string Username,
    string DisplayName,
    int WeeklyXp,
    int CurrentLevel,
    string LevelTitle,
    bool IsCurrentUser,
    bool IsFriend,
    bool CanViewProfile,
    string Zone);

public sealed record LeaderboardResponse(
    string Scope,
    string WeekStartDateLocal,
    string WeekEndDateLocal,
    string TimeZoneId,
    string CurrentTierCode,
    string CurrentTierName,
    int PromotionSlots,
    int DemotionSlots,
    bool IsParticipatingInLeaderboard,
    string? LastSettledWeekStartLocal,
    string LastWeekOutcome,
    int TotalParticipants,
    IReadOnlyList<LeaderboardEntryDto> Entries,
    LeaderboardEntryDto? CurrentUserEntry);
