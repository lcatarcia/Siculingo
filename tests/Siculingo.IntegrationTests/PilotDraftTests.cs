using System.Text.Json.Nodes;
using Siculingo.Infrastructure.Editorial;

namespace Siculingo.IntegrationTests;

public sealed class PilotDraftTests
{
    [Fact]
    public async Task Pilot_has_five_lessons_with_eight_to_twelve_valid_exercises_and_all_four_formats()
    {
        var draft = PilotDraft.Parse(await ReadDraft());
        Assert.Equal(5, draft.Lessons.Count);
        Assert.All(draft.Lessons, lesson =>
        {
            Assert.InRange(lesson.Exercises.Count, 8, 12);
            Assert.Contains(lesson.Exercises, exercise => exercise is ChoiceDraft);
            Assert.Contains(lesson.Exercises, exercise => exercise is MatchingDraft);
            Assert.Contains(lesson.Exercises, exercise => exercise is TranslationDraft);
            Assert.Contains(lesson.Exercises, exercise => exercise is FillBlankDraft);
        });
    }

    [Theory]
    [InlineData("unknown-status")]
    [InlineData("missing-lesson")]
    [InlineData("too-few-exercises")]
    [InlineData("duplicate-id")]
    [InlineData("invalid-choice")]
    [InlineData("duplicate-pair")]
    [InlineData("unavailable-token")]
    [InlineData("missing-gap")]
    public async Task Invalid_editorial_structure_cannot_be_imported(string scenario)
    {
        var json = JsonNode.Parse(await ReadDraft())!;
        var lessons = json["lessons"]!.AsArray();
        var exercises = lessons[0]!["exercises"]!.AsArray();
        switch (scenario)
        {
            case "unknown-status": json["reviewStatus"] = "auto-published"; break;
            case "missing-lesson": lessons.RemoveAt(4); break;
            case "too-few-exercises": exercises.RemoveAt(7); break;
            case "duplicate-id": exercises[1]!["id"] = exercises[0]!["id"]!.GetValue<string>(); break;
            case "invalid-choice": exercises[0]!["correctOptionIndex"] = 20; break;
            case "duplicate-pair": exercises[2]!["pairs"]![1]!["left"] = exercises[2]!["pairs"]![0]!["left"]!.GetValue<string>(); break;
            case "unavailable-token": exercises[4]!["answerTokens"]![0] = "tessera assente"; break;
            case "missing-gap": exercises[6]!["textWithGap"] = "nessuno spazio"; break;
        }
        Assert.Throws<InvalidDataException>(() => PilotDraft.Parse(json.ToJsonString()));
    }

    private static Task<string> ReadDraft() =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pilot-greetings.v1.json"));
}
