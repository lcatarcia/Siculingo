namespace Siculingo.Domain.Learning;

public sealed class CourseSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public required string Title { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
    public List<LearningUnit> Units { get; set; } = [];
}
