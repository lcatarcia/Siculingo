namespace Siculingo.Domain.Learning;

public sealed class LearningUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseSectionId { get; set; }
    public required string Title { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
    public List<Lesson> Lessons { get; set; } = [];
}
