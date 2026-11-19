namespace Siculingo.Domain.Administration;

public static class AdminRoles
{
    public const string Editor = "Editor";
    public const string Admin = "Admin";

    public static readonly Guid EditorRoleId = Guid.Parse("8d200001-0000-4000-8000-000000000001");
    public static readonly Guid AdminRoleId = Guid.Parse("8d200002-0000-4000-8000-000000000002");

    public const string EditorialPolicy = "EditorialAccess";
    public const string AdminPolicy = "AdminAccess";
}

public enum EditorialReviewState : byte
{
    Draft = 0,
    InReview = 1,
    Approved = 2
}

public enum LinguisticRegister : byte
{
    Informal = 0,
    Neutral = 1,
    Polite = 2,
    Colloquial = 3
}

public sealed class LinguisticLexeme
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Headword { get; set; }
    public required string MeaningIt { get; set; }
    public required string UsageContext { get; set; }
    public LinguisticRegister Register { get; set; } = LinguisticRegister.Informal;
    public string? OrthographicVariants { get; set; }
    public string? SourceReference { get; set; }
    public EditorialReviewState ReviewState { get; set; } = EditorialReviewState.Draft;
    public string? ReviewerNotes { get; set; }
    public Guid? LastEditedByUserId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
}

public enum ReportKind : byte
{
    ContentExercise = 0,
    UserBehavior = 1
}

public enum ReportStatus : byte
{
    Open = 0,
    InReview = 1,
    Resolved = 2,
    Dismissed = 3
}

public sealed class UserReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ReporterUserId { get; set; }
    public ReportKind Kind { get; set; }
    public Guid? TargetLessonId { get; set; }
    public string? TargetExerciseId { get; set; }
    public Guid? TargetUserId { get; set; }
    public required string ReasonCategory { get; set; }
    public required string Message { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public string? ResolutionNote { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}

public sealed class AdminAuditEntry
{
    public long Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string ActorUsername { get; set; }
    public required string ActionType { get; set; }
    public required string TargetKind { get; set; }
    public required string TargetIdentifier { get; set; }
    public required string Summary { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
