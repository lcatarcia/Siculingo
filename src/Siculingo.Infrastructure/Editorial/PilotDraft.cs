using System.Text.Json;
using System.Text.Json.Serialization;

namespace Siculingo.Infrastructure.Editorial;

public sealed record PilotDraft(
    int Version, string ReviewStatus, string EditorialNote, List<PilotLessonDraft> Lessons)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public const string PendingReviewStatus = "pending-linguistic-review";
    public const string ApprovedReviewStatus = "approved-by-owner";

    public static PilotDraft Parse(string json)
    {
        var draft = JsonSerializer.Deserialize<PilotDraft>(json, JsonOptions)
            ?? throw new InvalidDataException("Pilot draft is empty.");
        if (draft.Version != 1
            || (draft.ReviewStatus != PendingReviewStatus && draft.ReviewStatus != ApprovedReviewStatus)
            || string.IsNullOrWhiteSpace(draft.EditorialNote)
            || draft.Lessons is not { Count: 5 }
            || draft.Lessons.Any(lesson => lesson is null)
            || draft.Lessons.Select(lesson => lesson.Id).Distinct().Count() != 5)
            throw new InvalidDataException("Expected version 1, valid review status and five distinct lessons.");
        foreach (var lesson in draft.Lessons)
        {
            ValidateLesson(lesson);
        }
        return draft;
    }

    public static PilotLessonDraft ParseLesson(string json)
    {
        var lesson = JsonSerializer.Deserialize<PilotLessonDraft>(json, JsonOptions)
            ?? throw new InvalidDataException("Lesson snapshot is empty.");
        ValidateLesson(lesson);
        return lesson;
    }

    private static void ValidateLesson(PilotLessonDraft lesson)
    {
        if (lesson.Id == Guid.Empty || !ValidText(lesson.Title, 160)
            || !ValidText(lesson.Objective) || !ValidText(lesson.ReviewNote)
            || lesson.Exercises is not { Count: >= 8 and <= 12 }
            || lesson.Exercises.Any(exercise => exercise is null)
            || lesson.Exercises.Select(exercise => exercise.Id).Distinct().Count() != lesson.Exercises.Count)
            throw new InvalidDataException($"Invalid pilot lesson {lesson.Id}: title, review notes and 8-12 unique exercises required.");
        foreach (var exercise in lesson.Exercises)
        {
            if (!ValidText(exercise.Id, 40) || !ValidText(exercise.Prompt) || !ValidText(exercise.Explanation)
                || !exercise.IsValid())
                throw new InvalidDataException($"Invalid exercise {exercise.Id} in lesson {lesson.Id}.");
        }
    }

    internal static bool ValidText(string? value, int maxLength = 2000) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl);
}

public sealed record PilotLessonDraft(Guid Id, string Title, string Objective, string ReviewNote, List<ExerciseDraft> Exercises);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ChoiceDraft), "choice")]
[JsonDerivedType(typeof(MatchingDraft), "matching")]
[JsonDerivedType(typeof(TranslationDraft), "guidedTranslation")]
[JsonDerivedType(typeof(FillBlankDraft), "fillBlank")]
public abstract record ExerciseDraft(string Id, string Prompt, string Explanation)
{
    internal abstract bool IsValid();

    protected static bool ValidOptions(List<string>? options, int index) =>
        options is { Count: >= 2 and <= 8 } && options.All(value => PilotDraft.ValidText(value))
        && options.Distinct(StringComparer.OrdinalIgnoreCase).Count() == options.Count
        && index >= 0 && index < options.Count;
}

public sealed record ChoiceDraft(string Id, string Prompt, string Explanation, List<string> Options, int CorrectOptionIndex)
    : ExerciseDraft(Id, Prompt, Explanation)
{
    internal override bool IsValid() => ValidOptions(Options, CorrectOptionIndex);
}

public sealed record DraftPair(string Left, string Right);
public sealed record MatchingDraft(string Id, string Prompt, string Explanation, List<DraftPair> Pairs)
    : ExerciseDraft(Id, Prompt, Explanation)
{
    internal override bool IsValid() => Pairs is { Count: >= 2 and <= 6 }
        && Pairs.All(pair => pair is not null && PilotDraft.ValidText(pair.Left) && PilotDraft.ValidText(pair.Right))
        && Pairs.Select(pair => pair.Left).Distinct().Count() == Pairs.Count
        && Pairs.Select(pair => pair.Right).Distinct().Count() == Pairs.Count;
}

public sealed record TranslationDraft(string Id, string Prompt, string Explanation, List<string> Tokens, List<string> AnswerTokens)
    : ExerciseDraft(Id, Prompt, Explanation)
{
    internal override bool IsValid()
    {
        if (Tokens is not { Count: >= 2 and <= 16 } || AnswerTokens is not { Count: > 0 }
            || !Tokens.All(value => PilotDraft.ValidText(value)) || !AnswerTokens.All(value => PilotDraft.ValidText(value)))
            return false;
        var remaining = new List<string>(Tokens);
        return AnswerTokens.All(token => remaining.Remove(token));
    }
}

public sealed record FillBlankDraft(string Id, string Prompt, string Explanation, string TextWithGap,
    List<string> Options, int CorrectOptionIndex) : ExerciseDraft(Id, Prompt, Explanation)
{
    internal override bool IsValid() => PilotDraft.ValidText(TextWithGap)
        && TextWithGap.Split("___").Length == 2 && ValidOptions(Options, CorrectOptionIndex);
}
