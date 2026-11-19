using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Siculingo.Domain.Administration;
using Siculingo.Domain.Learning;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Infrastructure.Editorial;

public static class PilotDraftImporter
{
    public const string CourseCode = "ct-pilot-greetings-v1";

    public static async Task<bool> ImportAsync(SiculingoDbContext db, string json, CancellationToken cancellationToken = default)
    {
        var draft = PilotDraft.Parse(json);
        if (draft.ReviewStatus != PilotDraft.PendingReviewStatus)
            throw new InvalidOperationException("Draft import accepts only pending-linguistic-review content. Use publish-pilot for approved content.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await db.Courses.AsSplitQuery()
            .Include(course => course.Sections).ThenInclude(section => section.Units).ThenInclude(unit => unit.Lessons)
            .SingleOrDefaultAsync(course => course.Code == CourseCode, cancellationToken);
        if (existing is not null)
        {
            var sections = existing.Sections;
            var units = sections.SelectMany(section => section.Units).ToList();
            var lessons = units.SelectMany(unit => unit.Lessons).OrderBy(lesson => lesson.SortOrder).ToList();
            if (existing.IsPublished || sections.Any(section => section.IsPublished)
                || units.Any(unit => unit.IsPublished) || lessons.Any(lesson => lesson.IsPublished))
                throw new InvalidOperationException("The pilot contains published content and cannot be imported as a draft.");
            if (sections.Count != 1 || units.Count != 1 || lessons.Count != draft.Lessons.Count
                || lessons.Where((lesson, index) => lesson.Id != draft.Lessons[index].Id
                    || lesson.Title != draft.Lessons[index].Title
                    || lesson.EditorialDraftJson != SerializeLesson(draft.Lessons[index])).Any())
                throw new InvalidOperationException("Pilot drafts already exist with different content. No overwrite performed; reconcile editorial changes explicitly.");
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        db.Courses.Add(new Course
        {
            Code = CourseCode, Name = "Catanese: saluti e presentazioni",
            Sections =
            [
                new CourseSection
                {
                    Title = "Le prime chiacchiere",
                    Units =
                    [
                        new LearningUnit
                        {
                            Title = "Saluti e presentazioni",
                            Lessons = draft.Lessons.Select((lesson, index) => new Lesson
                            {
                                Id = lesson.Id, Title = lesson.Title, SortOrder = index + 1,
                                EditorialDraftJson = SerializeLesson(lesson)
                            }).ToList()
                        }
                    ]
                }
            ]
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public static async Task<bool> PublishApprovedAsync(SiculingoDbContext db, string json, CancellationToken cancellationToken = default)
    {
        var draft = PilotDraft.Parse(json);
        if (draft.ReviewStatus != PilotDraft.ApprovedReviewStatus)
            throw new InvalidOperationException("Only linguistically approved pilot content (reviewStatus = approved-by-owner) can be published.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await db.Courses.AsSplitQuery()
            .Include(course => course.Sections).ThenInclude(section => section.Units).ThenInclude(unit => unit.Lessons)
            .SingleOrDefaultAsync(course => course.Code == CourseCode, cancellationToken);

        if (existing is null)
        {
            db.Courses.Add(new Course
            {
                Code = CourseCode,
                Name = "Catanese: saluti e presentazioni",
                IsPublished = true,
                Sections =
                [
                    new CourseSection
                    {
                        Title = "Le prime chiacchiere",
                        IsPublished = true,
                        Units =
                        [
                            new LearningUnit
                            {
                                Title = "Saluti e presentazioni",
                                IsPublished = true,
                                Lessons = draft.Lessons.Select((lesson, index) =>
                                {
                                    var serialized = SerializeLesson(lesson);
                                    return new Lesson
                                    {
                                        Id = lesson.Id,
                                        Title = lesson.Title,
                                        SortOrder = index + 1,
                                        IsPublished = true,
                                        EditorialDraftJson = serialized,
                                        PublishedSnapshotJson = serialized,
                                        EditorialState = EditorialReviewState.Approved,
                                        ReviewerNotes = lesson.ReviewNote,
                                        ApprovedByName = "owner",
                                        ApprovedAtUtc = DateTimeOffset.UtcNow,
                                        PublishedAtUtc = DateTimeOffset.UtcNow
                                    };
                                }).ToList()
                            }
                        ]
                    }
                ]
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var sections = existing.Sections;
        var units = sections.SelectMany(section => section.Units).ToList();
        var lessons = units.SelectMany(unit => unit.Lessons).OrderBy(lesson => lesson.SortOrder).ToList();
        if (sections.Count != 1 || units.Count != 1 || lessons.Count != draft.Lessons.Count
            || lessons.Where((lesson, index) => lesson.Id != draft.Lessons[index].Id).Any())
            throw new InvalidOperationException("Existing pilot structure does not match the approved pilot lessons.");

        var changed = false;
        if (!existing.IsPublished) { existing.IsPublished = true; changed = true; }
        if (!sections[0].IsPublished) { sections[0].IsPublished = true; changed = true; }
        if (!units[0].IsPublished) { units[0].IsPublished = true; changed = true; }

        for (var index = 0; index < lessons.Count; index++)
        {
            var entity = lessons[index];
            var approvedLesson = draft.Lessons[index];
            var serialized = SerializeLesson(approvedLesson);
            if (entity.Title != approvedLesson.Title) { entity.Title = approvedLesson.Title; changed = true; }
            if (entity.EditorialDraftJson != serialized) { entity.EditorialDraftJson = serialized; changed = true; }
            if (entity.PublishedSnapshotJson != serialized) { entity.PublishedSnapshotJson = serialized; changed = true; }
            if (!entity.IsPublished) { entity.IsPublished = true; changed = true; }
            if (entity.EditorialState != EditorialReviewState.Approved) { entity.EditorialState = EditorialReviewState.Approved; changed = true; }
            if (entity.ReviewerNotes != approvedLesson.ReviewNote) { entity.ReviewerNotes = approvedLesson.ReviewNote; changed = true; }
            if (entity.ApprovedByName != "owner") { entity.ApprovedByName = "owner"; changed = true; }
            if (entity.ApprovedAtUtc is null) { entity.ApprovedAtUtc = DateTimeOffset.UtcNow; changed = true; }
            if (entity.PublishedAtUtc is null) { entity.PublishedAtUtc = DateTimeOffset.UtcNow; changed = true; }
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    public static string SerializeLesson(PilotLessonDraft lesson) => JsonSerializer.Serialize(lesson, PilotDraft.JsonOptions);
}
