using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Siculingo.Domain.Learning;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Learning;

public static class GetLearningPath
{
    public static void MapLearningPath(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/learning/path", HandleAsync)
            .RequireAuthorization().WithName("GetLearningPath").WithTags("Learning").ProducesProblem(401);
    }

    private static async Task<Results<Ok<LearningPathResponse>, UnauthorizedHttpResult>> HandleAsync(
        ClaimsPrincipal principal, SiculingoDbContext db, CancellationToken cancellationToken)
    {
        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
            return TypedResults.Unauthorized();

        var completedIds = (await db.UserLessonCompletions.AsNoTracking()
            .Where(completion => completion.UserId == userId)
            .Select(completion => completion.LessonId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var activeAttempt = await db.LessonAttempts.AsNoTracking()
            .Where(attempt => attempt.UserId == userId && attempt.Status == LessonAttemptStatus.InProgress)
            .Select(attempt => new
            {
                attempt.Id,
                attempt.LessonId,
                attempt.IsPractice,
                attempt.ExerciseCount,
                AnsweredCount = attempt.Answers.Count
            })
            .SingleOrDefaultAsync(cancellationToken);

        var rawCourses = await db.Courses.AsNoTracking()
            .AsSplitQuery()
            .Where(course => course.IsPublished)
            .OrderBy(course => course.Code)
            .Select(course => new
            {
                course.Id,
                course.Name,
                Sections = course.Sections.Where(section => section.IsPublished).OrderBy(section => section.SortOrder)
                    .Select(section => new
                    {
                        section.Id,
                        section.Title,
                        Units = section.Units.Where(unit => unit.IsPublished).OrderBy(unit => unit.SortOrder)
                            .Select(unit => new
                            {
                                unit.Id,
                                unit.Title,
                                Lessons = unit.Lessons.Where(lesson => lesson.IsPublished && lesson.PublishedSnapshotJson != null)
                                    .OrderBy(lesson => lesson.SortOrder)
                                    .Select(lesson => new { lesson.Id, lesson.Title }).ToList()
                            }).ToList()
                    }).ToList()
            })
            .ToListAsync(cancellationToken);

        var courses = new List<CoursePath>(rawCourses.Count);
        ActiveLessonRef? activeLessonRef = null;

        foreach (var course in rawCourses)
        {
            var previousUnlockedCompleted = true;
            var sections = new List<SectionPath>(course.Sections.Count);
            foreach (var section in course.Sections)
            {
                var units = new List<UnitPath>(section.Units.Count);
                foreach (var unit in section.Units)
                {
                    var lessons = new List<LessonPath>(unit.Lessons.Count);
                    foreach (var lesson in unit.Lessons)
                    {
                        var isCompleted = completedIds.Contains(lesson.Id);
                        string status;
                        if (isCompleted)
                        {
                            status = "completed";
                            previousUnlockedCompleted = true;
                        }
                        else if (previousUnlockedCompleted)
                        {
                            status = "available";
                            previousUnlockedCompleted = false;
                        }
                        else
                        {
                            status = "locked";
                        }

                        var inProgressId = activeAttempt?.LessonId == lesson.Id ? activeAttempt.Id : (Guid?)null;
                        if (inProgressId.HasValue && activeAttempt is not null)
                        {
                            activeLessonRef = new ActiveLessonRef(
                                activeAttempt.Id, lesson.Id, lesson.Title, activeAttempt.IsPractice, activeAttempt.AnsweredCount, activeAttempt.ExerciseCount);
                        }

                        lessons.Add(new LessonPath(lesson.Id, lesson.Title, status, inProgressId));
                    }
                    units.Add(new UnitPath(unit.Id, unit.Title, lessons));
                }
                sections.Add(new SectionPath(section.Id, section.Title, units));
            }
            courses.Add(new CoursePath(course.Id, course.Name, sections));
        }

        if (activeLessonRef is null && activeAttempt is not null)
        {
            var lessonTitle = await db.Lessons.AsNoTracking()
                .Where(lesson => lesson.Id == activeAttempt.LessonId)
                .Select(lesson => lesson.Title)
                .SingleOrDefaultAsync(cancellationToken) ?? "Lezione in corso";
            activeLessonRef = new ActiveLessonRef(
                activeAttempt.Id, activeAttempt.LessonId, lessonTitle, activeAttempt.IsPractice, activeAttempt.AnsweredCount, activeAttempt.ExerciseCount);
        }

        return TypedResults.Ok(new LearningPathResponse(courses, activeLessonRef));
    }
}

public sealed record LearningPathResponse(List<CoursePath> Courses, ActiveLessonRef? ActiveAttempt);
public sealed record ActiveLessonRef(Guid AttemptId, Guid LessonId, string LessonTitle, bool IsPractice, int AnsweredCount, int TotalExercises);
public sealed record CoursePath(Guid Id, string Name, List<SectionPath> Sections);
public sealed record SectionPath(Guid Id, string Title, List<UnitPath> Units);
public sealed record UnitPath(Guid Id, string Title, List<LessonPath> Lessons);
public sealed record LessonPath(Guid Id, string Title, string Status, Guid? InProgressAttemptId);
