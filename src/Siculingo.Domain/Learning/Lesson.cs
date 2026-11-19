using Siculingo.Domain.Administration;

namespace Siculingo.Domain.Learning;

public sealed class Lesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearningUnitId { get; set; }
    public required string Title { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
    public string? EditorialDraftJson { get; set; }
    public string? PublishedSnapshotJson { get; set; }
    public EditorialReviewState EditorialState { get; set; } = EditorialReviewState.Draft;
    public string? OrthographicVariants { get; set; }
    public string? SourceReference { get; set; }
    public string? ReviewerNotes { get; set; }
    public Guid? LastEditedByUserId { get; set; }
    public DateTimeOffset? LastEditedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}
