namespace Siculingo.Domain.Learning;

public enum LessonAttemptStatus
{
    InProgress = 0,
    Completed = 1,
    Abandoned = 2
}

public sealed class LessonAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid LessonId { get; set; }
    public bool IsPractice { get; set; }
    public LessonAttemptStatus Status { get; set; } = LessonAttemptStatus.InProgress;
    public required string LessonSnapshotJson { get; set; }
    public int ExerciseCount { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? AbandonedAtUtc { get; set; }
    public List<LessonAttemptAnswer> Answers { get; set; } = [];
}

public sealed class LessonAttemptAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LessonAttemptId { get; set; }
    public int StepIndex { get; set; }
    public required string ExerciseId { get; set; }
    public required string SubmittedPayloadJson { get; set; }
    public bool IsCorrect { get; set; }
    public required string Explanation { get; set; }
    public required string ExpectedDisplayAnswer { get; set; }
    public DateTimeOffset AnsweredAtUtc { get; set; }
}

public sealed class UserLessonCompletion
{
    public Guid UserId { get; set; }
    public Guid LessonId { get; set; }
    public DateTimeOffset FirstCompletedAtUtc { get; set; }
    public DateTimeOffset LastCompletedAtUtc { get; set; }
    public int CompletedCount { get; set; }
}
