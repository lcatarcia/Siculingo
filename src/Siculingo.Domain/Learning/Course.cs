namespace Siculingo.Domain.Learning;

public sealed class Course
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string SourceLanguage { get; set; } = "it";
    public string TargetDialect { get; set; } = "catanese";
    public bool IsPublished { get; set; }
    public List<CourseSection> Sections { get; set; } = [];
}
