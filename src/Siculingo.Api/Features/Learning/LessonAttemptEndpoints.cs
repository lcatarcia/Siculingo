using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siculingo.Api.Common;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Gamification;
using Siculingo.Domain.Learning;
using Siculingo.Infrastructure.Editorial;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Learning;

public static class LessonAttemptEndpoints
{
    public static void MapLessonAttempts(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/learning")
            .WithTags("Learning")
            .RequireAuthorization()
            .AddEndpointFilter<AccountAntiforgeryFilter>();

        group.MapPost("/lessons/{lessonId:guid}/attempts", StartOrResumeAttempt)
            .WithName("StartLessonAttempt")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/practice/start", StartPracticeAttempt)
            .WithName("StartPracticeSession")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapGet("/attempts/{attemptId:guid}", GetAttempt)
            .WithName("GetLessonAttempt")
            .ProducesProblem(401).ProducesProblem(404);

        group.MapPost("/attempts/{attemptId:guid}/answers", SubmitAnswer)
            .WithName("SubmitExerciseAnswer")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/attempts/{attemptId:guid}/complete", CompleteAttempt)
            .WithName("CompleteLessonAttempt")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/attempts/{attemptId:guid}/abandon", AbandonAttempt)
            .WithName("AbandonLessonAttempt")
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 2601 or 2627 })
                return true;
        }
        return false;
    }

    private static async Task<Results<Ok<LessonAttemptResponse>, UnauthorizedHttpResult, ProblemHttpResult>> StartPracticeAttempt(
        StartPracticeRequest? request, ClaimsPrincipal principal,
        SiculingoDbContext db, TimeProvider clock, IOptionsSnapshot<HeartsOptions> heartsOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var replaceActive = request?.ReplaceActive ?? false;
        if (!replaceActive)
        {
            var existingActivePractice = await db.LessonAttempts.AsNoTracking()
                .Where(item => item.UserId == userId && item.Status == LessonAttemptStatus.InProgress && item.IsPractice)
                .Select(item => (Guid?)item.LessonId)
                .SingleOrDefaultAsync(cancellationToken);

            if (existingActivePractice.HasValue)
            {
                return await StartAttemptInternalAsync(
                    existingActivePractice.Value,
                    replaceActive: false,
                    forcePractice: true,
                    userId,
                    db,
                    clock,
                    heartsOptions.Value.ConsumptionEnabled,
                    cancellationToken);
            }
        }

        var publishedLessonIds = await FindAllOrderedPublishedLessonIdsAsync(db, cancellationToken);
        if (publishedLessonIds.Count == 0)
        {
            return TypedResults.Problem(statusCode: 404, title: "Nessuna lezione disponibile",
                detail: "Non risultano lezioni pubblicate disponibili per il ripasso.");
        }

        var publishedSet = publishedLessonIds.ToHashSet();
        var completedRows = await db.UserLessonCompletions.AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.LastCompletedAtUtc)
            .ToListAsync(cancellationToken);

        var targetLessonId = completedRows
            .FirstOrDefault(item => publishedSet.Contains(item.LessonId))?.LessonId
            ?? publishedLessonIds[0];

        return await StartAttemptInternalAsync(
            targetLessonId,
            replaceActive,
            forcePractice: true,
            userId,
            db,
            clock,
            heartsOptions.Value.ConsumptionEnabled,
            cancellationToken);
    }

    private static async Task<Results<Ok<LessonAttemptResponse>, UnauthorizedHttpResult, ProblemHttpResult>> StartOrResumeAttempt(
        Guid lessonId, StartLessonAttemptRequest? request, ClaimsPrincipal principal,
        SiculingoDbContext db, TimeProvider clock, IOptionsSnapshot<HeartsOptions> heartsOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        return await StartAttemptInternalAsync(
            lessonId,
            request?.ReplaceActive ?? false,
            forcePractice: request?.PracticeMode ?? false,
            userId,
            db,
            clock,
            heartsOptions.Value.ConsumptionEnabled,
            cancellationToken);
    }

    private static async Task<Results<Ok<LessonAttemptResponse>, UnauthorizedHttpResult, ProblemHttpResult>> StartAttemptInternalAsync(
        Guid lessonId, bool replaceActive, bool forcePractice, Guid userId,
        SiculingoDbContext db, TimeProvider clock, bool heartsConsumptionEnabled,
        CancellationToken cancellationToken)
    {
        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                await using var transaction = await SqlConcurrencyLock.BeginUserTransactionAsync(db, userId, cancellationToken);
                var nowUtc = clock.GetUtcNow();

                var targetCourse = await FindPublishedCourseContainingLessonAsync(db, lessonId, cancellationToken);
                if (targetCourse is null)
                    return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                        detail: "La lezione richiesta non è disponibile o non è pubblicata.");

                var orderedLessons = targetCourse.OrderedLessons;
                var completedIds = (await db.UserLessonCompletions.AsNoTracking()
                    .Where(completion => completion.UserId == userId)
                    .Select(completion => completion.LessonId)
                    .ToListAsync(cancellationToken)).ToHashSet();

                if (!IsLessonUnlocked(orderedLessons, lessonId, completedIds))
                {
                    return TypedResults.Problem(statusCode: 403, title: "Lezione ancora bloccata",
                        detail: "Completa prima la lezione precedente del sentiero per sbloccare questa tappa.");
                }

                var isPractice = forcePractice || completedIds.Contains(lessonId);
                var trackedHearts = await HeartsService.GetOrCreateNormalizedTrackedAsync(db, userId, nowUtc, cancellationToken);
                var heartsStatus = HeartsService.BuildStatusDto(
                    trackedHearts.CurrentHearts,
                    trackedHearts.RegenerationAnchorUtc,
                    nowUtc,
                    heartsConsumptionEnabled);

                var active = await db.LessonAttempts
                    .Include(attempt => attempt.Answers)
                    .SingleOrDefaultAsync(attempt => attempt.UserId == userId && attempt.Status == LessonAttemptStatus.InProgress, cancellationToken);

                if (active is not null)
                {
                    if (active.LessonId == lessonId && (!forcePractice || active.IsPractice))
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return TypedResults.Ok(ToAttemptResponse(active, heartsStatus));
                    }

                    if (!replaceActive)
                    {
                        var activeSnapshot = PilotDraft.ParseLesson(active.LessonSnapshotJson);
                        await db.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return TypedResults.Problem(
                            statusCode: 409,
                            title: "Hai già una lezione iniziata",
                            detail: $"Stai svolgendo «{activeSnapshot.Title}». Vuoi riprenderla oppure chiuderla per iniziare questa sessione?");
                    }

                    active.Status = LessonAttemptStatus.Abandoned;
                    active.AbandonedAtUtc = nowUtc;
                }

                if (!isPractice && heartsConsumptionEnabled && heartsStatus.CurrentHearts <= 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return TypedResults.Problem(
                        statusCode: 409,
                        title: "Cuori esauriti",
                        detail: "Hai 0 cuori disponibili per avviare una nuova lezione del percorso. Avvia una sessione di ripasso per recuperare subito +1 cuore oppure attendi la rigenerazione (+1 ogni 4 ore).");
                }

                var snapshotJson = await db.Lessons.AsNoTracking()
                    .Where(lesson => lesson.Id == lessonId && lesson.IsPublished && lesson.PublishedSnapshotJson != null)
                    .Select(lesson => lesson.PublishedSnapshotJson)
                    .SingleOrDefaultAsync(cancellationToken);

                if (snapshotJson is null)
                {
                    return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                        detail: "La lezione richiesta non è disponibile o non è pubblicata.");
                }

                var parsedSnapshot = PilotDraft.ParseLesson(snapshotJson);
                var created = new LessonAttempt
                {
                    UserId = userId,
                    LessonId = lessonId,
                    IsPractice = isPractice,
                    Status = LessonAttemptStatus.InProgress,
                    LessonSnapshotJson = snapshotJson,
                    ExerciseCount = parsedSnapshot.Exercises.Count,
                    StartedAtUtc = nowUtc
                };
                db.LessonAttempts.Add(created);

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return TypedResults.Ok(ToAttemptResponse(created, heartsStatus));
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception) && retry < 2)
            {
                db.ChangeTracker.Clear();
            }
        }

        return TypedResults.Problem(statusCode: 409, title: "Tentativo già avviato",
            detail: "Riprova ad aprire la lezione.");
    }

    private static async Task<Results<Ok<LessonAttemptResponse>, UnauthorizedHttpResult, ProblemHttpResult>> GetAttempt(
        Guid attemptId, ClaimsPrincipal principal, SiculingoDbContext db,
        TimeProvider clock, IOptionsSnapshot<HeartsOptions> heartsOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        var attempt = await db.LessonAttempts.AsNoTracking()
            .Include(item => item.Answers)
            .SingleOrDefaultAsync(item => item.Id == attemptId && item.UserId == userId, cancellationToken);

        if (attempt is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Tentativo non trovato",
                detail: "Questo tentativo di lezione non esiste o non appartiene al tuo account.");
        }

        var hearts = await HeartsService.GetStatusSnapshotAsync(
            db,
            userId,
            clock.GetUtcNow(),
            heartsOptions.Value.ConsumptionEnabled,
            cancellationToken);

        return TypedResults.Ok(ToAttemptResponse(attempt, hearts));
    }

    private static async Task<Results<Ok<ExerciseSubmissionResponse>, UnauthorizedHttpResult, ProblemHttpResult>> SubmitAnswer(
        Guid attemptId, SubmitExerciseAnswerRequest request, ClaimsPrincipal principal,
        SiculingoDbContext db, TimeProvider clock, IOptionsSnapshot<HeartsOptions> heartsOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ExerciseId) || request.Answer is null)
        {
            return TypedResults.Problem(statusCode: 400, title: "Risposta non valida",
                detail: "Seleziona o componi una risposta prima di controllare.");
        }

        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                await using var transaction = await SqlConcurrencyLock.BeginUserTransactionAsync(db, userId, cancellationToken);
                var attempt = await db.LessonAttempts
                    .Include(item => item.Answers)
                    .SingleOrDefaultAsync(item => item.Id == attemptId && item.UserId == userId, cancellationToken);

                if (attempt is null)
                {
                    return TypedResults.Problem(statusCode: 404, title: "Tentativo non trovato",
                        detail: "Questo tentativo di lezione non esiste o non appartiene al tuo account.");
                }

                var snapshot = PilotDraft.ParseLesson(attempt.LessonSnapshotJson);
                var orderedAnswers = attempt.Answers.OrderBy(answer => answer.StepIndex).ToList();
                var alreadyAnswered = orderedAnswers.SingleOrDefault(answer => answer.ExerciseId == request.ExerciseId);
                if (alreadyAnswered is not null)
                {
                    var replayHearts = await HeartsService.GetStatusSnapshotAsync(
                        db,
                        userId,
                        clock.GetUtcNow(),
                        heartsOptions.Value.ConsumptionEnabled,
                        cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return TypedResults.Ok(ToSubmissionResponse(attempt, orderedAnswers.Count, alreadyAnswered, replayHearts, heartConsumed: false));
                }

                if (attempt.Status != LessonAttemptStatus.InProgress)
                {
                    return TypedResults.Problem(statusCode: 409, title: "Tentativo già concluso",
                        detail: "Questa sessione di esercizi non è più attiva.");
                }

                var nextIndex = orderedAnswers.Count;
                if (nextIndex >= snapshot.Exercises.Count)
                {
                    return TypedResults.Problem(statusCode: 409, title: "Esercizi già completati",
                        detail: "Hai già risposto a tutti gli esercizi: concludi la lezione per vedere il riepilogo.");
                }

                var expectedExercise = snapshot.Exercises[nextIndex];
                if (!string.Equals(expectedExercise.Id, request.ExerciseId, StringComparison.Ordinal))
                {
                    return TypedResults.Problem(statusCode: 409, title: "Ordine esercizi non allineato",
                        detail: "L'esercizio inviato non corrisponde al passo corrente della lezione.");
                }

                if (!TryGradeExercise(attempt.Id, expectedExercise, request.Answer, out var isCorrect, out var expectedDisplay, out var validationError))
                {
                    return TypedResults.Problem(statusCode: 400, title: "Formato risposta non valido",
                        detail: validationError);
                }

                var nowAnswerUtc = clock.GetUtcNow();
                var answerEntity = new LessonAttemptAnswer
                {
                    LessonAttemptId = attempt.Id,
                    StepIndex = nextIndex,
                    ExerciseId = expectedExercise.Id,
                    SubmittedPayloadJson = JsonSerializer.Serialize(request.Answer, PilotDraft.JsonOptions),
                    IsCorrect = isCorrect,
                    Explanation = expectedExercise.Explanation,
                    ExpectedDisplayAnswer = expectedDisplay,
                    AnsweredAtUtc = nowAnswerUtc
                };
                db.LessonAttemptAnswers.Add(answerEntity);
                await GamificationCalculator.ApplyAnswerPrecisionProgressAsync(
                    db,
                    userId,
                    isCorrect,
                    nowAnswerUtc,
                    cancellationToken);

                HeartsStatusDto heartsStatus;
                var heartConsumed = false;
                if (!isCorrect && !attempt.IsPractice)
                {
                    var normalizedBefore = await HeartsService.GetOrCreateNormalizedTrackedAsync(
                        db,
                        userId,
                        nowAnswerUtc,
                        cancellationToken);
                    var beforeCount = normalizedBefore.CurrentHearts;
                    heartsStatus = await HeartsService.ConsumeHeartOnWrongPathAnswerAsync(
                        db,
                        userId,
                        nowAnswerUtc,
                        heartsOptions.Value.ConsumptionEnabled,
                        cancellationToken);
                    heartConsumed = heartsStatus.CurrentHearts < beforeCount;
                }
                else
                {
                    var normalized = await HeartsService.GetOrCreateNormalizedTrackedAsync(
                        db,
                        userId,
                        nowAnswerUtc,
                        cancellationToken);
                    heartsStatus = HeartsService.BuildStatusDto(
                        normalized.CurrentHearts,
                        normalized.RegenerationAnchorUtc,
                        nowAnswerUtc,
                        heartsOptions.Value.ConsumptionEnabled);
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return TypedResults.Ok(ToSubmissionResponse(attempt, nextIndex + 1, answerEntity, heartsStatus, heartConsumed));
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception) && retry < 2)
            {
                db.ChangeTracker.Clear();
            }
        }

        return TypedResults.Problem(statusCode: 409, title: "Risposta già registrata",
            detail: "Ricarica la lezione per sincronizzare l'ultimo esercizio.");
    }

    private static async Task<Results<Ok<LessonCompletionResponse>, UnauthorizedHttpResult, ProblemHttpResult>> CompleteAttempt(
        Guid attemptId, ClaimsPrincipal principal, SiculingoDbContext db,
        TimeProvider clock, IOptionsSnapshot<HeartsOptions> heartsOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                await using var transaction = await SqlConcurrencyLock.BeginUserTransactionAsync(db, userId, cancellationToken);
                var attempt = await db.LessonAttempts
                    .Include(item => item.Answers)
                    .SingleOrDefaultAsync(item => item.Id == attemptId && item.UserId == userId, cancellationToken);

                if (attempt is null)
                {
                    return TypedResults.Problem(statusCode: 404, title: "Tentativo non trovato",
                        detail: "Questo tentativo di lezione non esiste o non appartiene al tuo account.");
                }

                if (attempt.Status == LessonAttemptStatus.Abandoned)
                {
                    return TypedResults.Problem(statusCode: 409, title: "Tentativo interrotto",
                        detail: "Questa lezione è stata interrotta e non può essere completata.");
                }

                var snapshot = PilotDraft.ParseLesson(attempt.LessonSnapshotJson);
                if (attempt.Answers.Count < snapshot.Exercises.Count)
                {
                    return TypedResults.Problem(statusCode: 409, title: "Lezione non ancora terminata",
                        detail: $"Rispondi a tutti gli esercizi ({attempt.Answers.Count}/{snapshot.Exercises.Count}) prima di concludere.");
                }

                var now = clock.GetUtcNow();
                var correctCount = attempt.Answers.Count(answer => answer.IsCorrect);
                var totalCount = snapshot.Exercises.Count;
                CompletionRewardOutcome rewardOutcome;
                bool lessonIsCompletedOnPath;

                if (attempt.Status == LessonAttemptStatus.InProgress)
                {
                    attempt.Status = LessonAttemptStatus.Completed;
                    attempt.CompletedAtUtc = now;

                    var existingCompletion = await db.UserLessonCompletions
                        .SingleOrDefaultAsync(item => item.UserId == userId && item.LessonId == attempt.LessonId, cancellationToken);
                    var isFirstCompletion = existingCompletion is null && !attempt.IsPractice;
                    if (existingCompletion is null)
                    {
                        if (!attempt.IsPractice)
                        {
                            db.UserLessonCompletions.Add(new UserLessonCompletion
                            {
                                UserId = userId,
                                LessonId = attempt.LessonId,
                                FirstCompletedAtUtc = now,
                                LastCompletedAtUtc = now,
                                CompletedCount = 1
                            });
                            lessonIsCompletedOnPath = true;
                        }
                        else
                        {
                            lessonIsCompletedOnPath = false;
                        }
                    }
                    else
                    {
                        existingCompletion.LastCompletedAtUtc = now;
                        existingCompletion.CompletedCount += 1;
                        lessonIsCompletedOnPath = true;
                    }

                    rewardOutcome = await GamificationCalculator.ApplyAttemptCompletionRewardsAsync(
                        db,
                        attempt,
                        isFirstCompletion,
                        correctCount,
                        totalCount,
                        now,
                        heartsOptions.Value.ConsumptionEnabled,
                        cancellationToken);

                    await db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    lessonIsCompletedOnPath = await db.UserLessonCompletions.AsNoTracking()
                        .AnyAsync(item => item.UserId == userId && item.LessonId == attempt.LessonId, cancellationToken);
                    rewardOutcome = await GamificationCalculator.ReadAttemptCompletionRewardsAsync(
                        db,
                        attempt,
                        now,
                        heartsOptions.Value.ConsumptionEnabled,
                        cancellationToken);
                }

                var nextUnlocked = lessonIsCompletedOnPath
                    ? await FindNextUnlockedLessonAsync(db, userId, attempt.LessonId, cancellationToken)
                    : null;
                await transaction.CommitAsync(cancellationToken);

                var accuracyPercent = (int)Math.Round(correctCount * 100.0 / totalCount, MidpointRounding.AwayFromZero);

                return TypedResults.Ok(new LessonCompletionResponse(
                    attempt.Id,
                    attempt.LessonId,
                    snapshot.Title,
                    attempt.IsPractice,
                    correctCount,
                    totalCount,
                    accuracyPercent,
                    attempt.CompletedAtUtc ?? now,
                    nextUnlocked?.Id,
                    nextUnlocked?.Title,
                    rewardOutcome));
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception) && retry < 2)
            {
                db.ChangeTracker.Clear();
            }
        }

        return TypedResults.Problem(statusCode: 409, title: "Completamento in corso",
            detail: "Riprova a visualizzare il riepilogo della lezione.");
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> AbandonAttempt(
        Guid attemptId, ClaimsPrincipal principal, SiculingoDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return TypedResults.Unauthorized();

        await using var transaction = await SqlConcurrencyLock.BeginUserTransactionAsync(db, userId, cancellationToken);
        var attempt = await db.LessonAttempts
            .SingleOrDefaultAsync(item => item.Id == attemptId && item.UserId == userId, cancellationToken);

        if (attempt is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Tentativo non trovato",
                detail: "Questo tentativo di lezione non esiste o non appartiene al tuo account.");
        }

        if (attempt.Status == LessonAttemptStatus.Completed)
        {
            return TypedResults.Problem(statusCode: 409, title: "Lezione già completata",
                detail: "La lezione è già stata conclusa e salvata.");
        }

        if (attempt.Status == LessonAttemptStatus.InProgress)
        {
            attempt.Status = LessonAttemptStatus.Abandoned;
            attempt.AbandonedAtUtc = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static LessonAttemptResponse ToAttemptResponse(LessonAttempt attempt, HeartsStatusDto hearts)
    {
        var snapshot = PilotDraft.ParseLesson(attempt.LessonSnapshotJson);
        var studentExercises = snapshot.Exercises
            .Select(exercise => ToStudentExercise(attempt.Id, exercise))
            .ToList();
        var recordedAnswers = attempt.Answers
            .OrderBy(answer => answer.StepIndex)
            .Select(answer => new RecordedAnswerDto(
                answer.StepIndex,
                answer.ExerciseId,
                answer.IsCorrect,
                answer.Explanation,
                answer.ExpectedDisplayAnswer))
            .ToList();

        var status = attempt.Status switch
        {
            LessonAttemptStatus.InProgress => "inProgress",
            LessonAttemptStatus.Completed => "completed",
            _ => "abandoned"
        };

        return new LessonAttemptResponse(
            attempt.Id,
            attempt.LessonId,
            snapshot.Title,
            snapshot.Objective,
            attempt.IsPractice,
            status,
            recordedAnswers.Count,
            studentExercises.Count,
            studentExercises,
            recordedAnswers,
            hearts);
    }

    private static ExerciseSubmissionResponse ToSubmissionResponse(
        LessonAttempt attempt,
        int answeredCount,
        LessonAttemptAnswer answer,
        HeartsStatusDto hearts,
        bool heartConsumed) =>
        new(
            attempt.Id,
            answer.ExerciseId,
            answer.StepIndex,
            answer.IsCorrect,
            answer.Explanation,
            answer.ExpectedDisplayAnswer,
            answeredCount,
            attempt.ExerciseCount,
            answeredCount >= attempt.ExerciseCount,
            heartConsumed,
            hearts);

    private static StudentExerciseDto ToStudentExercise(Guid attemptId, ExerciseDraft exercise) => exercise switch
    {
        ChoiceDraft choice => new StudentChoiceExercise(choice.Id, choice.Prompt, choice.Options),
        MatchingDraft matching => new StudentMatchingExercise(
            matching.Id,
            matching.Prompt,
            matching.Pairs.Select(pair => pair.Left).ToList(),
            ShuffleRightItems(attemptId, matching.Id, matching.Pairs.Select(pair => pair.Right).ToList())),
        TranslationDraft translation => new StudentGuidedTranslationExercise(
            translation.Id,
            translation.Prompt,
            translation.Tokens),
        FillBlankDraft fillBlank => new StudentFillBlankExercise(
            fillBlank.Id,
            fillBlank.Prompt,
            fillBlank.TextWithGap,
            fillBlank.Options),
        _ => throw new InvalidOperationException($"Unsupported exercise type {exercise.GetType().Name}.")
    };

    private static List<string> ShuffleRightItems(Guid attemptId, string exerciseId, List<string> items)
    {
        if (items.Count <= 1) return items;
        var ordered = items
            .Select((value, index) => (value, hash: HashCode.Combine(attemptId, exerciseId, index, value)))
            .OrderBy(entry => entry.hash)
            .ThenBy(entry => entry.value, StringComparer.Ordinal)
            .Select(entry => entry.value)
            .ToList();
        if (ordered.SequenceEqual(items, StringComparer.Ordinal))
        {
            var first = ordered[0];
            ordered.RemoveAt(0);
            ordered.Add(first);
        }
        return ordered;
    }

    private static bool TryGradeExercise(
        Guid attemptId,
        ExerciseDraft exercise,
        SubmittedExerciseAnswer answer,
        out bool isCorrect,
        out string expectedDisplay,
        out string validationError)
    {
        isCorrect = false;
        expectedDisplay = string.Empty;
        validationError = string.Empty;

        switch (exercise)
        {
            case ChoiceDraft choice:
                if (answer is not ChoiceAnswerSubmission choiceAnswer)
                {
                    validationError = "Per questo esercizio è richiesta una scelta multipla.";
                    return false;
                }
                if (choiceAnswer.SelectedOptionIndex < 0 || choiceAnswer.SelectedOptionIndex >= choice.Options.Count)
                {
                    validationError = "L'opzione selezionata non è valida.";
                    return false;
                }
                expectedDisplay = choice.Options[choice.CorrectOptionIndex];
                isCorrect = choiceAnswer.SelectedOptionIndex == choice.CorrectOptionIndex;
                return true;

            case FillBlankDraft fillBlank:
                if (answer is not FillBlankAnswerSubmission fillAnswer)
                {
                    validationError = "Per questo esercizio è richiesto il completamento della frase.";
                    return false;
                }
                if (fillAnswer.SelectedOptionIndex < 0 || fillAnswer.SelectedOptionIndex >= fillBlank.Options.Count)
                {
                    validationError = "L'opzione selezionata per lo spazio vuoto non è valida.";
                    return false;
                }
                expectedDisplay = fillBlank.TextWithGap.Replace("___", fillBlank.Options[fillBlank.CorrectOptionIndex], StringComparison.Ordinal);
                isCorrect = fillAnswer.SelectedOptionIndex == fillBlank.CorrectOptionIndex;
                return true;

            case MatchingDraft matching:
                if (answer is not MatchingAnswerSubmission matchingAnswer || matchingAnswer.Pairs is null)
                {
                    validationError = "Per questo esercizio devi abbinare tutte le coppie.";
                    return false;
                }
                var allowedLeft = matching.Pairs.Select(pair => pair.Left).ToHashSet(StringComparer.Ordinal);
                var allowedRight = ShuffleRightItems(attemptId, matching.Id, matching.Pairs.Select(pair => pair.Right).ToList())
                    .ToHashSet(StringComparer.Ordinal);
                if (matchingAnswer.Pairs.Count != matching.Pairs.Count
                    || matchingAnswer.Pairs.Any(pair => pair is null || string.IsNullOrWhiteSpace(pair.Left) || string.IsNullOrWhiteSpace(pair.Right))
                    || matchingAnswer.Pairs.Select(pair => pair.Left).Distinct(StringComparer.Ordinal).Count() != matching.Pairs.Count
                    || matchingAnswer.Pairs.Select(pair => pair.Right).Distinct(StringComparer.Ordinal).Count() != matching.Pairs.Count
                    || !matchingAnswer.Pairs.All(pair => allowedLeft.Contains(pair.Left) && allowedRight.Contains(pair.Right)))
                {
                    validationError = "Abbina ciascuna voce di sinistra a una voce distinta di destra.";
                    return false;
                }
                expectedDisplay = string.Join(" · ", matching.Pairs.Select(pair => $"{pair.Left} → {pair.Right}"));
                var expectedMap = matching.Pairs.ToDictionary(pair => pair.Left, pair => pair.Right, StringComparer.Ordinal);
                isCorrect = matchingAnswer.Pairs.All(pair => expectedMap.TryGetValue(pair.Left, out var expectedRight)
                    && string.Equals(expectedRight, pair.Right, StringComparison.Ordinal));
                return true;

            case TranslationDraft translation:
                if (answer is not GuidedTranslationAnswerSubmission translationAnswer
                    || translationAnswer.SelectedTokens is not { Count: > 0 })
                {
                    validationError = "Seleziona almeno una tessera per comporre la frase.";
                    return false;
                }
                var available = new List<string>(translation.Tokens);
                foreach (var token in translationAnswer.SelectedTokens)
                {
                    if (string.IsNullOrWhiteSpace(token) || !available.Remove(token))
                    {
                        validationError = "Le tessere scelte non appartengono a questo esercizio.";
                        return false;
                    }
                }
                expectedDisplay = string.Join(" ", translation.AnswerTokens);
                isCorrect = translationAnswer.SelectedTokens.SequenceEqual(translation.AnswerTokens, StringComparer.Ordinal);
                return true;

            default:
                validationError = "Formato esercizio non supportato.";
                return false;
        }
    }

    private sealed record PublishedLessonRow(Guid Id, string Title);
    private sealed record PublishedCourseOutline(List<PublishedLessonRow> OrderedLessons);

    private static async Task<List<Guid>> FindAllOrderedPublishedLessonIdsAsync(
        SiculingoDbContext db, CancellationToken cancellationToken)
    {
        return await (
            from course in db.Courses.AsNoTracking()
            where course.IsPublished
            from section in course.Sections
            where section.IsPublished
            from unit in section.Units
            where unit.IsPublished
            from lesson in unit.Lessons
            where lesson.IsPublished && lesson.PublishedSnapshotJson != null
            orderby course.Code, section.SortOrder, unit.SortOrder, lesson.SortOrder
            select lesson.Id)
            .ToListAsync(cancellationToken);
    }

    private static async Task<PublishedCourseOutline?> FindPublishedCourseContainingLessonAsync(
        SiculingoDbContext db, Guid lessonId, CancellationToken cancellationToken)
    {
        var flatLessons = await (
            from course in db.Courses.AsNoTracking()
            where course.IsPublished
            from section in course.Sections
            where section.IsPublished
            from unit in section.Units
            where unit.IsPublished
            from lesson in unit.Lessons
            where lesson.IsPublished && lesson.PublishedSnapshotJson != null
            orderby course.Code, section.SortOrder, unit.SortOrder, lesson.SortOrder
            select new
            {
                CourseId = course.Id,
                lesson.Id,
                lesson.Title
            })
            .ToListAsync(cancellationToken);

        foreach (var courseGroup in flatLessons.GroupBy(item => item.CourseId))
        {
            if (courseGroup.Any(item => item.Id == lessonId))
            {
                var ordered = courseGroup
                    .Select(item => new PublishedLessonRow(item.Id, item.Title))
                    .ToList();
                return new PublishedCourseOutline(ordered);
            }
        }

        return null;
    }

    private static bool IsLessonUnlocked(List<PublishedLessonRow> orderedLessons, Guid lessonId, HashSet<Guid> completedIds)
    {
        if (completedIds.Contains(lessonId)) return true;
        var previousCompleted = true;
        foreach (var lesson in orderedLessons)
        {
            var done = completedIds.Contains(lesson.Id);
            if (lesson.Id == lessonId)
                return previousCompleted;
            previousCompleted = done;
        }
        return false;
    }

    private static async Task<(Guid Id, string Title)?> FindNextUnlockedLessonAsync(
        SiculingoDbContext db, Guid userId, Guid completedLessonId, CancellationToken cancellationToken)
    {
        var outline = await FindPublishedCourseContainingLessonAsync(db, completedLessonId, cancellationToken);
        if (outline is null) return null;

        var completedIds = (await db.UserLessonCompletions.AsNoTracking()
            .Where(completion => completion.UserId == userId)
            .Select(completion => completion.LessonId)
            .ToListAsync(cancellationToken)).ToHashSet();
        completedIds.Add(completedLessonId);

        var currentIndex = outline.OrderedLessons.FindIndex(lesson => lesson.Id == completedLessonId);
        if (currentIndex < 0) return null;

        for (var index = currentIndex + 1; index < outline.OrderedLessons.Count; index++)
        {
            var candidate = outline.OrderedLessons[index];
            if (!completedIds.Contains(candidate.Id) && IsLessonUnlocked(outline.OrderedLessons, candidate.Id, completedIds))
                return (candidate.Id, candidate.Title);
        }
        return null;
    }
}

public sealed record StartLessonAttemptRequest(bool ReplaceActive = false, bool PracticeMode = false);
public sealed record StartPracticeRequest(bool ReplaceActive = false);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StudentChoiceExercise), "choice")]
[JsonDerivedType(typeof(StudentMatchingExercise), "matching")]
[JsonDerivedType(typeof(StudentGuidedTranslationExercise), "guidedTranslation")]
[JsonDerivedType(typeof(StudentFillBlankExercise), "fillBlank")]
public abstract record StudentExerciseDto(string Id, string Prompt);

public sealed record StudentChoiceExercise(string Id, string Prompt, List<string> Options)
    : StudentExerciseDto(Id, Prompt);

public sealed record StudentMatchingExercise(string Id, string Prompt, List<string> LeftItems, List<string> RightItems)
    : StudentExerciseDto(Id, Prompt);

public sealed record StudentGuidedTranslationExercise(string Id, string Prompt, List<string> Tokens)
    : StudentExerciseDto(Id, Prompt);

public sealed record StudentFillBlankExercise(string Id, string Prompt, string TextWithGap, List<string> Options)
    : StudentExerciseDto(Id, Prompt);

public sealed record RecordedAnswerDto(
    int StepIndex, string ExerciseId, bool IsCorrect, string Explanation, string ExpectedDisplayAnswer);

public sealed record LessonAttemptResponse(
    Guid AttemptId,
    Guid LessonId,
    string LessonTitle,
    string LessonObjective,
    bool IsPractice,
    string Status,
    int CurrentStepIndex,
    int TotalExercises,
    List<StudentExerciseDto> Exercises,
    List<RecordedAnswerDto> RecordedAnswers,
    HeartsStatusDto Hearts);

public sealed record MatchingPairInput(string Left, string Right);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ChoiceAnswerSubmission), "choice")]
[JsonDerivedType(typeof(MatchingAnswerSubmission), "matching")]
[JsonDerivedType(typeof(GuidedTranslationAnswerSubmission), "guidedTranslation")]
[JsonDerivedType(typeof(FillBlankAnswerSubmission), "fillBlank")]
public abstract record SubmittedExerciseAnswer;

public sealed record ChoiceAnswerSubmission(int SelectedOptionIndex) : SubmittedExerciseAnswer;
public sealed record MatchingAnswerSubmission(List<MatchingPairInput> Pairs) : SubmittedExerciseAnswer;
public sealed record GuidedTranslationAnswerSubmission(List<string> SelectedTokens) : SubmittedExerciseAnswer;
public sealed record FillBlankAnswerSubmission(int SelectedOptionIndex) : SubmittedExerciseAnswer;

public sealed record SubmitExerciseAnswerRequest(string ExerciseId, SubmittedExerciseAnswer Answer);

public sealed record ExerciseSubmissionResponse(
    Guid AttemptId,
    string ExerciseId,
    int StepIndex,
    bool IsCorrect,
    string Explanation,
    string ExpectedDisplayAnswer,
    int AnsweredCount,
    int TotalExercises,
    bool IsReadyToComplete,
    bool HeartConsumed,
    HeartsStatusDto Hearts);

public sealed record LessonCompletionResponse(
    Guid AttemptId,
    Guid LessonId,
    string LessonTitle,
    bool IsPractice,
    int CorrectAnswers,
    int TotalExercises,
    int AccuracyPercent,
    DateTimeOffset CompletedAtUtc,
    Guid? NextUnlockedLessonId,
    string? NextUnlockedLessonTitle,
    CompletionRewardOutcome Rewards);
