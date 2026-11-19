using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Accounts;
using Siculingo.Domain.Administration;
using Siculingo.Domain.Learning;
using Siculingo.Infrastructure.Accounts;
using Siculingo.Infrastructure.Editorial;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Administration;

public static class AdministrationEndpoints
{
    private static readonly HashSet<string> AllowedReportReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "ortografia",
        "traduzione",
        "spiegazione",
        "variante",
        "altro"
    };

    public static void MapAdministration(this IEndpointRouteBuilder endpoints)
    {
        // Student-facing report endpoint (authenticated)
        var studentReports = endpoints.MapGroup("/api/v1/reports")
            .WithTags("Reports")
            .RequireAuthorization()
            .AddEndpointFilter<AccountAntiforgeryFilter>();

        studentReports.MapPost("/exercises", CreateExerciseReport)
            .WithName("CreateExerciseReport")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // Editorial access group (Editor + Admin)
        var editorial = endpoints.MapGroup("/api/v1/admin")
            .WithTags("Administration")
            .RequireAuthorization(AdminRoles.EditorialPolicy)
            .AddEndpointFilter<AccountAntiforgeryFilter>();

        editorial.MapGet("/overview", GetAdminOverview)
            .WithName("GetAdminOverview")
            .ProducesProblem(401)
            .ProducesProblem(403);

        editorial.MapGet("/catalog", GetEditorialCatalog)
            .WithName("GetEditorialCatalog")
            .ProducesProblem(401)
            .ProducesProblem(403);

        editorial.MapGet("/lessons/{lessonId:guid}", GetEditorialLesson)
            .WithName("GetEditorialLesson")
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        editorial.MapPost("/units/{unitId:guid}/lessons", CreateLessonDraft)
            .WithName("CreateLessonDraft")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        editorial.MapPost("/lessons/{lessonId:guid}/draft", SaveLessonDraft)
            .WithName("SaveLessonDraft")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        editorial.MapPost("/lessons/{lessonId:guid}/submit-review", SubmitLessonForReview)
            .WithName("SubmitLessonForReview")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        editorial.MapGet("/lexemes", ListLexemes)
            .WithName("ListLinguisticLexemes")
            .ProducesProblem(401)
            .ProducesProblem(403);

        editorial.MapPost("/lexemes", UpsertLexeme)
            .WithName("UpsertLinguisticLexeme")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // Admin / Reviewer-only endpoints (Admin)
        var adminOnly = endpoints.MapGroup("/api/v1/admin")
            .WithTags("Administration")
            .RequireAuthorization(AdminRoles.AdminPolicy)
            .AddEndpointFilter<AccountAntiforgeryFilter>();

        adminOnly.MapPost("/lessons/{lessonId:guid}/review", ReviewLessonDraft)
            .WithName("ReviewLessonDraft")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        adminOnly.MapPost("/lessons/{lessonId:guid}/publish", PublishApprovedLesson)
            .WithName("PublishApprovedLesson")
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        adminOnly.MapPost("/lessons/{lessonId:guid}/unpublish", UnpublishLesson)
            .WithName("UnpublishLesson")
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        adminOnly.MapPost("/lexemes/{lexemeId:guid}/review", ReviewLexeme)
            .WithName("ReviewLinguisticLexeme")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        adminOnly.MapGet("/reports", ListReports)
            .WithName("ListUserReports")
            .ProducesProblem(401)
            .ProducesProblem(403);

        adminOnly.MapPost("/reports/{reportId:guid}/resolve", ResolveReport)
            .WithName("ResolveUserReport")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        adminOnly.MapGet("/users", ListManagedUsers)
            .WithName("ListManagedUsers")
            .ProducesProblem(401)
            .ProducesProblem(403);

        adminOnly.MapPost("/users/{targetUserId:guid}/suspension", SetUserSuspension)
            .WithName("SetUserSuspension")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        adminOnly.MapPost("/users/{targetUserId:guid}/roles", UpdateUserRole)
            .WithName("UpdateUserRole")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        adminOnly.MapGet("/audit", ListAuditEntries)
            .WithName("ListAdminAuditEntries")
            .ProducesProblem(401)
            .ProducesProblem(403);

        adminOnly.MapGet("/product-metrics", ProductMetricsEndpoint.HandleAsync)
            .WithName("GetProductMetrics")
            .WithSummary("Metriche di prodotto pseudonimizzate con denominatori e finestra espliciti per la valutazione della beta.")
            .ProducesProblem(401)
            .ProducesProblem(403);
    }

    public static async Task GrantRoleCliAsync(
        IServiceProvider services,
        string rawArgument,
        ILogger logger)
    {
        var parts = rawArgument.Split([':', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("Use --grant-role \"<username>:<Editor|Admin>\" (for example --grant-role \"lorenzo:Admin\").");
        }

        var username = parts[0];
        var roleName = NormalizeRoleName(parts[1])
            ?? throw new InvalidOperationException($"Role '{parts[1]}' is invalid. Supported roles: {AdminRoles.Editor}, {AdminRoles.Admin}.");

        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<SiculingoDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        if (!await roles.RoleExistsAsync(roleName))
        {
            throw new InvalidOperationException($"Role '{roleName}' does not exist in database. Apply migrations first.");
        }

        var user = await users.FindByNameAsync(username)
            ?? throw new InvalidOperationException($"Account '{username}' was not found. Register the account first.");

        var alreadyAssigned = await users.IsInRoleAsync(user, roleName);
        if (!alreadyAssigned)
        {
            var addResult = await users.AddToRoleAsync(user, roleName);
            if (!addResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to grant role '{roleName}': {string.Join("; ", addResult.Errors.Select(e => e.Description))}");
            }

            await users.UpdateSecurityStampAsync(user);
            db.AdminAuditEntries.Add(new AdminAuditEntry
            {
                ActorUserId = user.Id,
                ActorUsername = user.UserName ?? username,
                ActionType = "cli.role.granted",
                TargetKind = "user",
                TargetIdentifier = user.UserName ?? username,
                Summary = $"Ruolo {roleName} assegnato da comando CLI locale a {user.UserName} (effettua nuovamente il login per aggiornare il cookie).",
                CreatedAtUtc = clock.GetUtcNow()
            });
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Grant-role CLI: user '{Username}' {Outcome} role '{Role}'. Log in again in the browser to refresh role claims.",
            user.UserName, alreadyAssigned ? "already had" : "was granted", roleName);
    }

    private static async Task<Results<Ok<ReportCreatedResponse>, UnauthorizedHttpResult, ProblemHttpResult>> CreateExerciseReport(
        CreateExerciseReportRequest request,
        ClaimsPrincipal principal,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var reporterId))
            return TypedResults.Unauthorized();

        var category = request.ReasonCategory?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(category) || !AllowedReportReasons.Contains(category))
        {
            return TypedResults.Problem(statusCode: 400, title: "Categoria non valida",
                detail: "Scegli una categoria tra ortografia, traduzione, spiegazione, variante o altro.");
        }

        var message = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message) || message.Length is < 6 or > 1500 || message.Any(c => char.IsControl(c) && c is not '\n' and not '\r'))
        {
            return TypedResults.Problem(statusCode: 400, title: "Descrizione non valida",
                detail: "Descrivi la segnalazione usando da 6 a 1500 caratteri.");
        }

        var exerciseId = request.ExerciseId?.Trim();
        if (string.IsNullOrWhiteSpace(exerciseId) || exerciseId.Length > 40 || exerciseId.Any(char.IsControl))
        {
            return TypedResults.Problem(statusCode: 400, title: "Esercizio non valido",
                detail: "Identificativo dell'esercizio mancante o non valido.");
        }

        var lessonExists = await db.Lessons.AsNoTracking()
            .AnyAsync(lesson => lesson.Id == request.LessonId && lesson.IsPublished, cancellationToken);
        if (!lessonExists)
        {
            return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                detail: "La lezione segnalata non esiste o non è pubblicata.");
        }

        var now = clock.GetUtcNow();
        var report = new UserReport
        {
            ReporterUserId = reporterId,
            Kind = ReportKind.ContentExercise,
            TargetLessonId = request.LessonId,
            TargetExerciseId = exerciseId,
            ReasonCategory = category,
            Message = message,
            Status = ReportStatus.Open,
            CreatedAtUtc = now
        };

        db.UserReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ReportCreatedResponse(report.Id, FormatReportStatus(report.Status), report.CreatedAtUtc));
    }

    private static async Task<Results<Ok<AdminOverviewResponse>, UnauthorizedHttpResult>> GetAdminOverview(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var actorRoles = await users.GetRolesAsync(actor);
        var isAdmin = actorRoles.Any(role => string.Equals(role, AdminRoles.Admin, StringComparison.OrdinalIgnoreCase));

        var totalUsers = await db.Users.CountAsync(cancellationToken);
        var suspendedUsers = await db.Users.CountAsync(u => u.IsSuspended, cancellationToken);
        var publishedLessons = await db.Lessons.CountAsync(l => l.IsPublished && l.PublishedSnapshotJson != null, cancellationToken);
        var inReviewLessons = await db.Lessons.CountAsync(l => l.EditorialState == EditorialReviewState.InReview, cancellationToken);
        var draftLessons = await db.Lessons.CountAsync(l => !l.IsPublished, cancellationToken);
        var totalLexemes = await db.LinguisticLexemes.CountAsync(cancellationToken);
        var approvedLexemes = await db.LinguisticLexemes.CountAsync(x => x.ReviewState == EditorialReviewState.Approved, cancellationToken);
        var completedAttempts = await db.LessonAttempts.CountAsync(a => a.Status == LessonAttemptStatus.Completed, cancellationToken);
        var openReports = isAdmin
            ? await db.UserReports.CountAsync(r => r.Status == ReportStatus.Open || r.Status == ReportStatus.InReview, cancellationToken)
            : 0;

        return TypedResults.Ok(new AdminOverviewResponse(
            isAdmin ? "admin" : "editor",
            totalUsers,
            suspendedUsers,
            publishedLessons,
            inReviewLessons,
            draftLessons,
            totalLexemes,
            approvedLexemes,
            completedAttempts,
            openReports));
    }

    private static async Task<Ok<EditorialCatalogResponse>> GetEditorialCatalog(
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        var courses = await db.Courses.AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Sections)
                .ThenInclude(s => s.Units)
                    .ThenInclude(u => u.Lessons)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken);

        var mappedCourses = courses.Select(course => new EditorialCourseDto(
            course.Id,
            course.Code,
            course.Name,
            course.IsPublished,
            course.Sections
                .OrderBy(s => s.SortOrder)
                .Select(section => new EditorialSectionDto(
                    section.Id,
                    section.Title,
                    section.SortOrder,
                    section.IsPublished,
                    section.Units
                        .OrderBy(u => u.SortOrder)
                        .Select(unit => new EditorialUnitDto(
                            unit.Id,
                            unit.Title,
                            unit.SortOrder,
                            unit.IsPublished,
                            unit.Lessons
                                .OrderBy(l => l.SortOrder)
                                .Select(ToLessonSummaryDto)
                                .ToList()))
                        .ToList()))
                .ToList())).ToList();

        return TypedResults.Ok(new EditorialCatalogResponse(mappedCourses));
    }

    private static async Task<Results<Ok<EditorialLessonDetailResponse>, ProblemHttpResult>> GetEditorialLesson(
        Guid lessonId,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        var lesson = await db.Lessons.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == lessonId, cancellationToken);

        if (lesson is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                detail: "La lezione richiesta non esiste nel catalogo editoriale.");
        }

        var sourceJson = lesson.EditorialDraftJson ?? lesson.PublishedSnapshotJson;
        PilotLessonDraft? parsedDraft = sourceJson is not null ? PilotDraft.ParseLesson(sourceJson) : null;

        return TypedResults.Ok(new EditorialLessonDetailResponse(
            ToLessonSummaryDto(lesson),
            parsedDraft,
            sourceJson ?? string.Empty));
    }

    private static async Task<Results<Ok<EditorialLessonSummaryDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateLessonDraft(
        Guid unitId,
        CreateLessonDraftRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var unit = await db.LearningUnits
            .Include(u => u.Lessons)
            .SingleOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        if (unit is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Unità non trovata",
                detail: "L'unità selezionata non esiste.");
        }

        var title = request.Title?.Trim();
        var objective = request.Objective?.Trim();
        var reviewNote = request.ReviewNote?.Trim();
        if (!CleanLine(title, 160) || !CleanLine(objective, 500) || !CleanText(reviewNote, 2000))
        {
            return TypedResults.Problem(statusCode: 400, title: "Dati lezione non validi",
                detail: "Inserisci titolo (max 160), obiettivo didattico (max 500) e nota di revisione linguistica.");
        }

        var lessonId = Guid.NewGuid();
        PilotLessonDraft parsedDraft;
        if (!string.IsNullOrWhiteSpace(request.DraftJson))
        {
            try
            {
                var candidate = PilotDraft.ParseLesson(request.DraftJson);
                parsedDraft = candidate with { Id = lessonId, Title = title!, Objective = objective!, ReviewNote = reviewNote! };
                _ = PilotDraft.ParseLesson(PilotDraftImporter.SerializeLesson(parsedDraft));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                return TypedResults.Problem(statusCode: 400, title: "JSON esercizi non valido",
                    detail: ex.Message);
            }
        }
        else
        {
            parsedDraft = BuildStarterLessonDraft(lessonId, title!, objective!, reviewNote!);
        }

        var serialized = PilotDraftImporter.SerializeLesson(parsedDraft);
        var nextSortOrder = unit.Lessons.Count == 0 ? 1 : unit.Lessons.Max(l => l.SortOrder) + 1;
        var now = clock.GetUtcNow();

        var lesson = new Lesson
        {
            Id = lessonId,
            LearningUnitId = unitId,
            Title = parsedDraft.Title,
            SortOrder = nextSortOrder,
            IsPublished = false,
            EditorialDraftJson = serialized,
            PublishedSnapshotJson = null,
            EditorialState = EditorialReviewState.Draft,
            OrthographicVariants = NormalizeOptional(request.OrthographicVariants, 500),
            SourceReference = NormalizeOptional(request.SourceReference, 500),
            ReviewerNotes = parsedDraft.ReviewNote,
            LastEditedByUserId = actor.Id,
            LastEditedAtUtc = now
        };

        db.Lessons.Add(lesson);
        AddAudit(db, actor, "lesson.draft.created", "lesson", lesson.Id.ToString(),
            $"Creata nuova bozza lezione «{lesson.Title}» (ordine {lesson.SortOrder}, non pubblicata).", now);

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToLessonSummaryDto(lesson));
    }

    private static async Task<Results<Ok<EditorialLessonDetailResponse>, UnauthorizedHttpResult, ProblemHttpResult>> SaveLessonDraft(
        Guid lessonId,
        SaveLessonDraftRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var lesson = await db.Lessons.SingleOrDefaultAsync(item => item.Id == lessonId, cancellationToken);
        if (lesson is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                detail: "La lezione da aggiornare non esiste.");
        }

        if (!ValidateOptionalText(request.OrthographicVariants, 500) || !ValidateOptionalText(request.SourceReference, 500))
        {
            return TypedResults.Problem(statusCode: 400, title: "Metadati non validi",
                detail: "Varianti ortografiche e fonti possono contenere al massimo 500 caratteri ciascuno.");
        }

        PilotLessonDraft parsedDraft;
        try
        {
            var candidate = PilotDraft.ParseLesson(request.DraftJson ?? string.Empty);
            parsedDraft = candidate with { Id = lesson.Id };
            // Re-validate after ensuring ID matches the route lesson
            parsedDraft = PilotDraft.ParseLesson(PilotDraftImporter.SerializeLesson(parsedDraft));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return TypedResults.Problem(statusCode: 400, title: "Bozza lezione non valida",
                detail: "La bozza deve rispettare il formato JSON previsto (titolo, obiettivo, nota di revisione e 8–12 esercizi validi senza duplicati). Dettaglio: " + ex.Message);
        }

        var serialized = PilotDraftImporter.SerializeLesson(parsedDraft);
        var now = clock.GetUtcNow();
        var draftChanged = !string.Equals(lesson.EditorialDraftJson, serialized, StringComparison.Ordinal);

        lesson.EditorialDraftJson = serialized;
        lesson.OrthographicVariants = NormalizeOptional(request.OrthographicVariants, 500);
        lesson.SourceReference = NormalizeOptional(request.SourceReference, 500);
        lesson.LastEditedByUserId = actor.Id;
        lesson.LastEditedAtUtc = now;

        // If the lesson is unpublished, mirror the title immediately; if already published, keep live Title & PublishedSnapshotJson untouched until re-approved and republished!
        if (!lesson.IsPublished)
        {
            lesson.Title = parsedDraft.Title;
        }

        if (draftChanged)
        {
            lesson.EditorialState = EditorialReviewState.Draft;
            lesson.ApprovedByUserId = null;
            lesson.ApprovedByName = null;
            lesson.ApprovedAtUtc = null;
        }

        AddAudit(db, actor, "lesson.draft.saved", "lesson", lesson.Id.ToString(),
            $"Salvata bozza di «{parsedDraft.Title}»{(draftChanged ? " (richiede nuova revisione linguistica prima della pubblicazione)" : " (metadati aggiornati)")}.",
            now);

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new EditorialLessonDetailResponse(
            ToLessonSummaryDto(lesson),
            parsedDraft,
            serialized));
    }

    private static async Task<Results<Ok<EditorialLessonSummaryDto>, UnauthorizedHttpResult, ProblemHttpResult>> SubmitLessonForReview(
        Guid lessonId,
        SubmitReviewRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var lesson = await db.Lessons.SingleOrDefaultAsync(item => item.Id == lessonId, cancellationToken);
        if (lesson is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                detail: "La lezione indicata non esiste.");
        }

        if (string.IsNullOrWhiteSpace(lesson.EditorialDraftJson))
        {
            return TypedResults.Problem(statusCode: 400, title: "Bozza mancante",
                detail: "Salva una bozza valida da 8–12 esercizi prima di inviarla in revisione linguistica.");
        }

        try
        {
            _ = PilotDraft.ParseLesson(lesson.EditorialDraftJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return TypedResults.Problem(statusCode: 400, title: "Bozza non valida", detail: ex.Message);
        }

        var note = NormalizeOptional(request.Note, 2000);
        var now = clock.GetUtcNow();
        lesson.EditorialState = EditorialReviewState.InReview;
        if (note is not null)
            lesson.ReviewerNotes = note;
        lesson.LastEditedByUserId = actor.Id;
        lesson.LastEditedAtUtc = now;

        AddAudit(db, actor, "lesson.review.submitted", "lesson", lesson.Id.ToString(),
            $"Lezione «{lesson.Title}» inviata in revisione linguistica.", now);

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToLessonSummaryDto(lesson));
    }

    private static async Task<Results<Ok<EditorialLessonSummaryDto>, UnauthorizedHttpResult, ProblemHttpResult>> ReviewLessonDraft(
        Guid lessonId,
        ReviewLessonRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var lesson = await db.Lessons.SingleOrDefaultAsync(item => item.Id == lessonId, cancellationToken);
        if (lesson is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                detail: "La lezione da revisionare non esiste.");
        }

        if (string.IsNullOrWhiteSpace(lesson.EditorialDraftJson))
        {
            return TypedResults.Problem(statusCode: 400, title: "Bozza assente",
                detail: "Non esiste una bozza editoriale da sottoporre a revisione.");
        }

        var reviewerNotes = request.ReviewerNotes?.Trim();
        if (!CleanText(reviewerNotes, 2000))
        {
            return TypedResults.Problem(statusCode: 400, title: "Nota di revisione obbligatoria",
                detail: "Inserisci una nota linguistica del revisore (fino a 2000 caratteri) per approvare o rimandare in bozza.");
        }

        PilotLessonDraft parsedDraft;
        try
        {
            parsedDraft = PilotDraft.ParseLesson(lesson.EditorialDraftJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return TypedResults.Problem(statusCode: 400, title: "Bozza non conforme", detail: ex.Message);
        }

        var now = clock.GetUtcNow();
        lesson.ReviewerNotes = reviewerNotes;

        if (request.Approved)
        {
            lesson.EditorialState = EditorialReviewState.Approved;
            lesson.ApprovedByUserId = actor.Id;
            lesson.ApprovedByName = actor.UserName ?? "admin";
            lesson.ApprovedAtUtc = now;
            AddAudit(db, actor, "lesson.review.approved", "lesson", lesson.Id.ToString(),
                $"Approvazione linguistica confermata per «{parsedDraft.Title}».", now);
        }
        else
        {
            lesson.EditorialState = EditorialReviewState.Draft;
            lesson.ApprovedByUserId = null;
            lesson.ApprovedByName = null;
            lesson.ApprovedAtUtc = null;
            AddAudit(db, actor, "lesson.review.changes_requested", "lesson", lesson.Id.ToString(),
                $"Richieste modifiche linguistiche sulla bozza di «{parsedDraft.Title}».", now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToLessonSummaryDto(lesson));
    }

    private static async Task<Results<Ok<EditorialLessonSummaryDto>, UnauthorizedHttpResult, ProblemHttpResult>> PublishApprovedLesson(
        Guid lessonId,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var lesson = await db.Lessons.SingleOrDefaultAsync(item => item.Id == lessonId, cancellationToken);
        if (lesson is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                detail: "La lezione da pubblicare non esiste.");
        }

        if (lesson.EditorialState != EditorialReviewState.Approved || string.IsNullOrWhiteSpace(lesson.EditorialDraftJson))
        {
            return TypedResults.Problem(statusCode: 409, title: "Revisione linguistica richiesta",
                detail: "Una lezione o una modifica in bozza può essere pubblicata solo dopo approvazione linguistica esplicita.");
        }

        var parsedDraft = PilotDraft.ParseLesson(lesson.EditorialDraftJson);
        var unit = await db.LearningUnits.SingleAsync(u => u.Id == lesson.LearningUnitId, cancellationToken);
        var section = await db.CourseSections.SingleAsync(s => s.Id == unit.CourseSectionId, cancellationToken);
        var course = await db.Courses.SingleAsync(c => c.Id == section.CourseId, cancellationToken);

        var now = clock.GetUtcNow();
        lesson.Title = parsedDraft.Title;
        lesson.PublishedSnapshotJson = lesson.EditorialDraftJson;
        lesson.IsPublished = true;
        lesson.PublishedAtUtc = now;

        // Ensure parent unit/section/course are published when publishing an approved lesson inside them
        unit.IsPublished = true;
        section.IsPublished = true;
        course.IsPublished = true;

        AddAudit(db, actor, "lesson.published", "lesson", lesson.Id.ToString(),
            $"Pubblicata lezione approvata «{lesson.Title}» sul sentiero studenti (i tentativi già avviati mantengono il loro snapshot immutabile).", now);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(ToLessonSummaryDto(lesson));
    }

    private static async Task<Results<Ok<EditorialLessonSummaryDto>, UnauthorizedHttpResult, ProblemHttpResult>> UnpublishLesson(
        Guid lessonId,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var lesson = await db.Lessons.SingleOrDefaultAsync(item => item.Id == lessonId, cancellationToken);
        if (lesson is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Lezione non trovata",
                detail: "La lezione indicata non esiste.");
        }

        var now = clock.GetUtcNow();
        lesson.IsPublished = false;
        AddAudit(db, actor, "lesson.unpublished", "lesson", lesson.Id.ToString(),
            $"Rimossa dalla pubblicazione la lezione «{lesson.Title}» (eventuali tentativi già in corso non vengono corrotti).", now);

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToLessonSummaryDto(lesson));
    }

    private static async Task<Ok<LinguisticLexemeListResponse>> ListLexemes(
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        var items = await db.LinguisticLexemes.AsNoTracking()
            .OrderBy(item => item.Headword)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new LinguisticLexemeListResponse(
            items.Select(ToLexemeDto).ToList()));
    }

    private static async Task<Results<Ok<LinguisticLexemeDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpsertLexeme(
        UpsertLinguisticLexemeRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var headword = request.Headword?.Trim();
        var meaningIt = request.MeaningIt?.Trim();
        var usageContext = request.UsageContext?.Trim();

        if (!CleanLine(headword, 120) || !CleanLine(meaningIt, 300) || !CleanText(usageContext, 600))
        {
            return TypedResults.Problem(statusCode: 400, title: "Voce lessicale non valida",
                detail: "Inserisci forma catanese (max 120), significato italiano (max 300) e contesto d'uso (max 600).");
        }

        if (!TryParseRegister(request.Register, out var parsedRegister))
        {
            return TypedResults.Problem(statusCode: 400, title: "Registro non valido",
                detail: "Scegli un registro tra informale, neutro, cortese o colloquiale.");
        }

        if (!ValidateOptionalText(request.OrthographicVariants, 500) || !ValidateOptionalText(request.SourceReference, 500))
        {
            return TypedResults.Problem(statusCode: 400, title: "Varianti o fonte troppo lunghe",
                detail: "Varianti e fonti accettano fino a 500 caratteri ciascuna.");
        }

        var now = clock.GetUtcNow();
        LinguisticLexeme entity;
        if (request.Id is { } existingId && existingId != Guid.Empty)
        {
            var found = await db.LinguisticLexemes.SingleOrDefaultAsync(item => item.Id == existingId, cancellationToken);
            if (found is null)
            {
                return TypedResults.Problem(statusCode: 404, title: "Voce non trovata",
                    detail: "La voce lessicale da modificare non esiste.");
            }

            entity = found;
            entity.Headword = headword!;
            entity.MeaningIt = meaningIt!;
            entity.UsageContext = usageContext!;
            entity.Register = parsedRegister;
            entity.OrthographicVariants = NormalizeOptional(request.OrthographicVariants, 500);
            entity.SourceReference = NormalizeOptional(request.SourceReference, 500);
            entity.ReviewState = request.SubmitForReview ? EditorialReviewState.InReview : EditorialReviewState.Draft;
            entity.LastEditedByUserId = actor.Id;
            entity.UpdatedAtUtc = now;
            entity.ReviewedByUserId = null;
            entity.ReviewedAtUtc = null;

            AddAudit(db, actor, "lexeme.updated", "lexeme", entity.Id.ToString(),
                $"Aggiornata voce lessicale «{entity.Headword}» ({FormatEditorialState(entity.ReviewState)}).", now);
        }
        else
        {
            entity = new LinguisticLexeme
            {
                Headword = headword!,
                MeaningIt = meaningIt!,
                UsageContext = usageContext!,
                Register = parsedRegister,
                OrthographicVariants = NormalizeOptional(request.OrthographicVariants, 500),
                SourceReference = NormalizeOptional(request.SourceReference, 500),
                ReviewState = request.SubmitForReview ? EditorialReviewState.InReview : EditorialReviewState.Draft,
                LastEditedByUserId = actor.Id,
                UpdatedAtUtc = now
            };
            db.LinguisticLexemes.Add(entity);
            AddAudit(db, actor, "lexeme.created", "lexeme", entity.Id.ToString(),
                $"Aggiunta voce lessicale «{entity.Headword}» ({FormatEditorialState(entity.ReviewState)}).", now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToLexemeDto(entity));
    }

    private static async Task<Results<Ok<LinguisticLexemeDto>, UnauthorizedHttpResult, ProblemHttpResult>> ReviewLexeme(
        Guid lexemeId,
        ReviewLexemeRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var lexeme = await db.LinguisticLexemes.SingleOrDefaultAsync(item => item.Id == lexemeId, cancellationToken);
        if (lexeme is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Voce non trovata",
                detail: "La voce lessicale indicata non esiste.");
        }

        var notes = NormalizeOptional(request.ReviewerNotes, 2000);
        var now = clock.GetUtcNow();
        lexeme.ReviewState = request.Approved ? EditorialReviewState.Approved : EditorialReviewState.Draft;
        lexeme.ReviewerNotes = notes;
        lexeme.ReviewedByUserId = actor.Id;
        lexeme.ReviewedAtUtc = now;
        lexeme.UpdatedAtUtc = now;

        AddAudit(db, actor, request.Approved ? "lexeme.approved" : "lexeme.returned_to_draft", "lexeme", lexeme.Id.ToString(),
            $"{(request.Approved ? "Approvata" : "Rimandata in bozza")} voce lessicale «{lexeme.Headword}».", now);

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToLexemeDto(lexeme));
    }

    private static async Task<Ok<AdminReportListResponse>> ListReports(
        string? status,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.UserReports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && TryParseReportStatus(status, out var parsedStatus))
        {
            query = query.Where(item => item.Status == parsedStatus);
        }

        var rawReports = await query
            .OrderBy(item => item.Status == ReportStatus.Open || item.Status == ReportStatus.InReview ? 0 : 1)
            .ThenByDescending(item => item.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var userIds = rawReports.Where(r => r.ReporterUserId.HasValue).Select(r => r.ReporterUserId!.Value)
            .Concat(rawReports.Where(r => r.ResolvedByUserId.HasValue).Select(r => r.ResolvedByUserId!.Value))
            .Distinct()
            .ToList();

        var lessonIds = rawReports.Where(r => r.TargetLessonId.HasValue)
            .Select(r => r.TargetLessonId!.Value)
            .Distinct()
            .ToList();

        var usernames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? "utente", cancellationToken);

        var lessonTitles = await db.Lessons.AsNoTracking()
            .Where(l => lessonIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Title, cancellationToken);

        var mapped = rawReports.Select(r => new AdminReportDto(
            r.Id,
            FormatReportKind(r.Kind),
            FormatReportStatus(r.Status),
            r.ReporterUserId,
            r.ReporterUserId.HasValue ? usernames.GetValueOrDefault(r.ReporterUserId.Value, "[account eliminato]") : "[account eliminato]",
            r.TargetLessonId,
            r.TargetLessonId.HasValue ? lessonTitles.GetValueOrDefault(r.TargetLessonId.Value) : null,
            r.TargetExerciseId,
            r.ReasonCategory,
            r.Message,
            r.ResolutionNote,
            r.ResolvedByUserId.HasValue ? usernames.GetValueOrDefault(r.ResolvedByUserId.Value) : null,
            r.CreatedAtUtc,
            r.ResolvedAtUtc)).ToList();

        return TypedResults.Ok(new AdminReportListResponse(mapped));
    }

    private static async Task<Results<Ok<AdminReportDto>, UnauthorizedHttpResult, ProblemHttpResult>> ResolveReport(
        Guid reportId,
        ResolveUserReportRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        if (!TryParseReportStatus(request.Status, out var targetStatus) || targetStatus == ReportStatus.Open)
        {
            return TypedResults.Problem(statusCode: 400, title: "Stato non valido",
                detail: "Scegli uno stato tra inReview, resolved o dismissed.");
        }

        var note = NormalizeOptional(request.ResolutionNote, 1500);
        if ((targetStatus == ReportStatus.Resolved || targetStatus == ReportStatus.Dismissed) && string.IsNullOrWhiteSpace(note))
        {
            return TypedResults.Problem(statusCode: 400, title: "Nota di gestione richiesta",
                detail: "Indica una breve nota di gestione quando chiudi o archivi una segnalazione.");
        }

        var report = await db.UserReports.SingleOrDefaultAsync(item => item.Id == reportId, cancellationToken);
        if (report is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Segnalazione non trovata",
                detail: "La segnalazione richiesta non esiste.");
        }

        var now = clock.GetUtcNow();
        report.Status = targetStatus;
        report.ResolutionNote = note;
        report.ResolvedByUserId = actor.Id;
        report.ResolvedAtUtc = targetStatus is ReportStatus.Resolved or ReportStatus.Dismissed ? now : null;

        AddAudit(db, actor, $"report.{FormatReportStatus(targetStatus)}", "report", report.Id.ToString(),
            $"Segnalazione su esercizio {report.TargetExerciseId ?? "-"} aggiornata a {FormatReportStatus(targetStatus)}.", now);

        await db.SaveChangesAsync(cancellationToken);

        var reporterName = report.ReporterUserId.HasValue
            ? await db.Users.AsNoTracking()
                .Where(u => u.Id == report.ReporterUserId.Value)
                .Select(u => u.UserName)
                .SingleOrDefaultAsync(cancellationToken) ?? "[account eliminato]"
            : "[account eliminato]";

        var lessonTitle = report.TargetLessonId.HasValue
            ? await db.Lessons.AsNoTracking().Where(l => l.Id == report.TargetLessonId.Value).Select(l => l.Title).SingleOrDefaultAsync(cancellationToken)
            : null;

        return TypedResults.Ok(new AdminReportDto(
            report.Id,
            FormatReportKind(report.Kind),
            FormatReportStatus(report.Status),
            report.ReporterUserId,
            reporterName,
            report.TargetLessonId,
            lessonTitle,
            report.TargetExerciseId,
            report.ReasonCategory,
            report.Message,
            report.ResolutionNote,
            actor.UserName,
            report.CreatedAtUtc,
            report.ResolvedAtUtc));
    }

    private static async Task<Ok<ManagedUserListResponse>> ListManagedUsers(
        string? search,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        var usersQuery = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            usersQuery = usersQuery.Where(u =>
                (u.UserName != null && u.UserName.Contains(term)) ||
                (u.DisplayName != null && u.DisplayName.Contains(term)));
        }

        var userRows = await usersQuery
            .OrderByDescending(u => u.RegisteredAtUtc)
            .Take(80)
            .ToListAsync(cancellationToken);

        var userIds = userRows.Select(u => u.Id).ToList();

        var roleAssignments = await db.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(
                db.Roles.AsNoTracking(),
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, RoleName = r.Name ?? string.Empty })
            .ToListAsync(cancellationToken);

        var rolesByUser = roleAssignments
            .GroupBy(item => item.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.RoleName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var statsByUser = await db.UserStats.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, s => s.TotalXp, cancellationToken);

        var items = userRows.Select(u => new ManagedUserDto(
            u.Id,
            u.UserName ?? "utente",
            u.DisplayName ?? u.UserName ?? "utente",
            rolesByUser.GetValueOrDefault(u.Id, []),
            statsByUser.GetValueOrDefault(u.Id, 0),
            u.IsSuspended,
            u.SuspensionReason,
            u.SuspendedAtUtc,
            u.RegisteredAtUtc)).ToList();

        return TypedResults.Ok(new ManagedUserListResponse(items));
    }

    private static async Task<Results<Ok<ManagedUserDto>, UnauthorizedHttpResult, ProblemHttpResult>> SetUserSuspension(
        Guid targetUserId,
        SetUserSuspensionRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        if (actor.Id == targetUserId)
        {
            return TypedResults.Problem(statusCode: 409, title: "Operazione non consentita",
                detail: "Non puoi sospendere il tuo stesso account amministratore.");
        }

        var target = await users.FindByIdAsync(targetUserId.ToString());
        if (target is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Utente non trovato",
                detail: "L'account selezionato non esiste.");
        }

        var reason = NormalizeOptional(request.Reason, 500);
        if (request.Suspend && string.IsNullOrWhiteSpace(reason))
        {
            return TypedResults.Problem(statusCode: 400, title: "Motivazione obbligatoria",
                detail: "Indica la motivazione della sospensione (fino a 500 caratteri).");
        }

        var now = clock.GetUtcNow();
        target.IsSuspended = request.Suspend;
        target.SuspensionReason = request.Suspend ? reason : null;
        target.SuspendedAtUtc = request.Suspend ? now : null;
        target.SuspendedByUserId = request.Suspend ? actor.Id : null;

        var updateResult = await users.UpdateAsync(target);
        if (!updateResult.Succeeded)
        {
            return TypedResults.Problem(statusCode: 409, title: "Aggiornamento non riuscito",
                detail: "Impossibile salvare lo stato di sospensione.");
        }

        // Revoke all active cookies immediately when suspending
        await users.UpdateSecurityStampAsync(target);

        AddAudit(db, actor, request.Suspend ? "user.suspended" : "user.reinstated", "user", target.UserName ?? target.Id.ToString(),
            request.Suspend
                ? $"Sospeso account @{target.UserName}: {reason}"
                : $"Riattivato account @{target.UserName}.",
            now);

        await db.SaveChangesAsync(cancellationToken);

        var targetRoles = await users.GetRolesAsync(target);
        var totalXp = await db.UserStats.AsNoTracking()
            .Where(s => s.UserId == target.Id)
            .Select(s => (int?)s.TotalXp)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        return TypedResults.Ok(new ManagedUserDto(
            target.Id,
            target.UserName ?? "utente",
            target.DisplayName ?? target.UserName ?? "utente",
            targetRoles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
            totalXp,
            target.IsSuspended,
            target.SuspensionReason,
            target.SuspendedAtUtc,
            target.RegisteredAtUtc));
    }

    private static async Task<Results<Ok<ManagedUserDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateUserRole(
        Guid targetUserId,
        UpdateUserRoleRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SiculingoDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(principal);
        if (actor is null)
            return TypedResults.Unauthorized();

        var roleName = NormalizeRoleName(request.Role);
        if (roleName is null)
        {
            return TypedResults.Problem(statusCode: 400, title: "Ruolo non valido",
                detail: $"Ruoli supportati: {AdminRoles.Editor}, {AdminRoles.Admin}.");
        }

        if (actor.Id == targetUserId && !request.Grant && roleName == AdminRoles.Admin)
        {
            return TypedResults.Problem(statusCode: 409, title: "Operazione bloccata",
                detail: "Non puoi rimuovere il ruolo Admin dal tuo stesso account in sessione.");
        }

        var target = await users.FindByIdAsync(targetUserId.ToString());
        if (target is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Utente non trovato",
                detail: "L'account selezionato non esiste.");
        }

        var hasRole = await users.IsInRoleAsync(target, roleName);
        var now = clock.GetUtcNow();

        if (request.Grant && !hasRole)
        {
            var add = await users.AddToRoleAsync(target, roleName);
            if (!add.Succeeded)
                return TypedResults.Problem(statusCode: 409, title: "Assegnazione ruolo fallita", detail: "Riprova.");
            await users.UpdateSecurityStampAsync(target);
            AddAudit(db, actor, "user.role.granted", "user", target.UserName ?? target.Id.ToString(),
                $"Assegnato ruolo {roleName} a @{target.UserName}.", now);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (!request.Grant && hasRole)
        {
            var remove = await users.RemoveFromRoleAsync(target, roleName);
            if (!remove.Succeeded)
                return TypedResults.Problem(statusCode: 409, title: "Rimozione ruolo fallita", detail: "Riprova.");
            await users.UpdateSecurityStampAsync(target);
            AddAudit(db, actor, "user.role.revoked", "user", target.UserName ?? target.Id.ToString(),
                $"Revocato ruolo {roleName} da @{target.UserName}.", now);
            await db.SaveChangesAsync(cancellationToken);
        }

        var targetRoles = await users.GetRolesAsync(target);
        var totalXp = await db.UserStats.AsNoTracking()
            .Where(s => s.UserId == target.Id)
            .Select(s => (int?)s.TotalXp)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        return TypedResults.Ok(new ManagedUserDto(
            target.Id,
            target.UserName ?? "utente",
            target.DisplayName ?? target.UserName ?? "utente",
            targetRoles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
            totalXp,
            target.IsSuspended,
            target.SuspensionReason,
            target.SuspendedAtUtc,
            target.RegisteredAtUtc));
    }

    private static async Task<Ok<AdminAuditListResponse>> ListAuditEntries(
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        var entries = await db.AdminAuditEntries.AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(80)
            .Select(item => new AdminAuditEntryDto(
                item.Id,
                item.ActorUserId,
                item.ActorUsername,
                item.ActionType,
                item.TargetKind,
                item.TargetIdentifier,
                item.Summary,
                item.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AdminAuditListResponse(entries));
    }

    private static EditorialLessonSummaryDto ToLessonSummaryDto(Lesson lesson)
    {
        var hasUnpublishedChanges = lesson.IsPublished &&
            !string.IsNullOrWhiteSpace(lesson.EditorialDraftJson) &&
            !string.Equals(lesson.EditorialDraftJson, lesson.PublishedSnapshotJson, StringComparison.Ordinal);

        var draftSource = lesson.EditorialDraftJson ?? lesson.PublishedSnapshotJson;
        var exerciseCount = 0;
        string? draftTitle = null;
        string? objective = null;
        if (!string.IsNullOrWhiteSpace(draftSource))
        {
            try
            {
                var parsed = PilotDraft.ParseLesson(draftSource);
                exerciseCount = parsed.Exercises.Count;
                draftTitle = parsed.Title;
                objective = parsed.Objective;
            }
            catch
            {
                exerciseCount = 0;
            }
        }

        return new EditorialLessonSummaryDto(
            lesson.Id,
            lesson.LearningUnitId,
            lesson.Title,
            draftTitle ?? lesson.Title,
            objective,
            lesson.SortOrder,
            lesson.IsPublished,
            hasUnpublishedChanges,
            FormatEditorialState(lesson.EditorialState),
            exerciseCount,
            lesson.OrthographicVariants,
            lesson.SourceReference,
            lesson.ReviewerNotes,
            lesson.ApprovedByName,
            lesson.LastEditedAtUtc,
            lesson.ApprovedAtUtc,
            lesson.PublishedAtUtc);
    }

    private static LinguisticLexemeDto ToLexemeDto(LinguisticLexeme item) =>
        new(
            item.Id,
            item.Headword,
            item.MeaningIt,
            item.UsageContext,
            FormatRegister(item.Register),
            item.OrthographicVariants,
            item.SourceReference,
            FormatEditorialState(item.ReviewState),
            item.ReviewerNotes,
            item.UpdatedAtUtc,
            item.ReviewedAtUtc);

    private static PilotLessonDraft BuildStarterLessonDraft(Guid lessonId, string title, string objective, string reviewNote)
    {
        var exercises = Enumerable.Range(1, 8)
            .Select(index => (ExerciseDraft)new ChoiceDraft(
                $"bozza-{index:00}",
                $"Esercizio {index} da compilare ({title})",
                "Nota esplicativa da completare in redazione prima della revisione linguistica.",
                ["Opzione A (corretta provvisoria)", "Opzione B", "Opzione C"],
                0))
            .ToList();

        return new PilotLessonDraft(lessonId, title, objective, reviewNote, exercises);
    }

    private static void AddAudit(
        SiculingoDbContext db,
        ApplicationUser actor,
        string actionType,
        string targetKind,
        string targetIdentifier,
        string summary,
        DateTimeOffset nowUtc)
    {
        db.AdminAuditEntries.Add(new AdminAuditEntry
        {
            ActorUserId = actor.Id,
            ActorUsername = actor.UserName ?? "staff",
            ActionType = actionType,
            TargetKind = targetKind,
            TargetIdentifier = targetIdentifier,
            Summary = summary,
            CreatedAtUtc = nowUtc
        });
    }

    private static string? NormalizeRoleName(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "editor" => AdminRoles.Editor,
        "admin" => AdminRoles.Admin,
        _ => null
    };

    private static string FormatEditorialState(EditorialReviewState state) => state switch
    {
        EditorialReviewState.Draft => "draft",
        EditorialReviewState.InReview => "inReview",
        EditorialReviewState.Approved => "approved",
        _ => "draft"
    };

    private static string FormatRegister(LinguisticRegister register) => register switch
    {
        LinguisticRegister.Informal => "informale",
        LinguisticRegister.Neutral => "neutro",
        LinguisticRegister.Polite => "cortese",
        LinguisticRegister.Colloquial => "colloquiale",
        _ => "informale"
    };

    private static bool TryParseRegister(string? raw, out LinguisticRegister register)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "informale":
                register = LinguisticRegister.Informal;
                return true;
            case "neutro":
                register = LinguisticRegister.Neutral;
                return true;
            case "cortese":
                register = LinguisticRegister.Polite;
                return true;
            case "colloquiale":
                register = LinguisticRegister.Colloquial;
                return true;
            default:
                register = LinguisticRegister.Informal;
                return false;
        }
    }

    private static string FormatReportKind(ReportKind kind) => kind switch
    {
        ReportKind.ContentExercise => "contentExercise",
        ReportKind.UserBehavior => "userBehavior",
        _ => "contentExercise"
    };

    private static string FormatReportStatus(ReportStatus status) => status switch
    {
        ReportStatus.Open => "open",
        ReportStatus.InReview => "inReview",
        ReportStatus.Resolved => "resolved",
        ReportStatus.Dismissed => "dismissed",
        _ => "open"
    };

    private static bool TryParseReportStatus(string? raw, out ReportStatus status)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "open":
                status = ReportStatus.Open;
                return true;
            case "inreview":
                status = ReportStatus.InReview;
                return true;
            case "resolved":
                status = ReportStatus.Resolved;
                return true;
            case "dismissed":
                status = ReportStatus.Dismissed;
                return true;
            default:
                status = ReportStatus.Open;
                return false;
        }
    }

    private static bool CleanLine(string? value, int maxLen) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLen && !value.Any(char.IsControl);

    private static bool CleanText(string? value, int maxLen) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLen && !value.Any(c => char.IsControl(c) && c is not '\n' and not '\r');

    private static bool ValidateOptionalText(string? value, int maxLen) =>
        value is null || (value.Trim().Length <= maxLen && !value.Any(c => char.IsControl(c) && c is not '\n' and not '\r'));

    private static string? NormalizeOptional(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}

public sealed record CreateExerciseReportRequest(Guid LessonId, string ExerciseId, string ReasonCategory, string Message);
public sealed record ReportCreatedResponse(Guid ReportId, string Status, DateTimeOffset CreatedAtUtc);

public sealed record AdminOverviewResponse(
    string AccessLevel,
    int TotalUsers,
    int SuspendedUsers,
    int PublishedLessons,
    int InReviewLessons,
    int DraftLessons,
    int TotalLexemes,
    int ApprovedLexemes,
    int CompletedAttempts,
    int OpenReports);

public sealed record EditorialCatalogResponse(IReadOnlyList<EditorialCourseDto> Courses);
public sealed record EditorialCourseDto(Guid Id, string Code, string Name, bool IsPublished, IReadOnlyList<EditorialSectionDto> Sections);
public sealed record EditorialSectionDto(Guid Id, string Title, int SortOrder, bool IsPublished, IReadOnlyList<EditorialUnitDto> Units);
public sealed record EditorialUnitDto(Guid Id, string Title, int SortOrder, bool IsPublished, IReadOnlyList<EditorialLessonSummaryDto> Lessons);

public sealed record EditorialLessonSummaryDto(
    Guid Id,
    Guid LearningUnitId,
    string PublishedOrCurrentTitle,
    string DraftTitle,
    string? Objective,
    int SortOrder,
    bool IsPublished,
    bool HasUnpublishedChanges,
    string EditorialState,
    int ExerciseCount,
    string? OrthographicVariants,
    string? SourceReference,
    string? ReviewerNotes,
    string? ApprovedByName,
    DateTimeOffset? LastEditedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? PublishedAtUtc);

public sealed record EditorialLessonDetailResponse(
    EditorialLessonSummaryDto Metadata,
    PilotLessonDraft? Draft,
    string DraftJson);

public sealed record CreateLessonDraftRequest(
    string Title,
    string Objective,
    string ReviewNote,
    string? OrthographicVariants = null,
    string? SourceReference = null,
    string? DraftJson = null);

public sealed record SaveLessonDraftRequest(
    string DraftJson,
    string? OrthographicVariants = null,
    string? SourceReference = null);

public sealed record SubmitReviewRequest(string? Note = null);
public sealed record ReviewLessonRequest(bool Approved, string ReviewerNotes);

public sealed record LinguisticLexemeListResponse(IReadOnlyList<LinguisticLexemeDto> Items);
public sealed record LinguisticLexemeDto(
    Guid Id,
    string Headword,
    string MeaningIt,
    string UsageContext,
    string Register,
    string? OrthographicVariants,
    string? SourceReference,
    string ReviewState,
    string? ReviewerNotes,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ReviewedAtUtc);

public sealed record UpsertLinguisticLexemeRequest(
    Guid? Id,
    string Headword,
    string MeaningIt,
    string UsageContext,
    string Register,
    string? OrthographicVariants,
    string? SourceReference,
    bool SubmitForReview);

public sealed record ReviewLexemeRequest(bool Approved, string? ReviewerNotes);

public sealed record AdminReportListResponse(IReadOnlyList<AdminReportDto> Items);
public sealed record AdminReportDto(
    Guid Id,
    string Kind,
    string Status,
    Guid? ReporterUserId,
    string ReporterUsername,
    Guid? TargetLessonId,
    string? TargetLessonTitle,
    string? TargetExerciseId,
    string ReasonCategory,
    string Message,
    string? ResolutionNote,
    string? ResolvedByUsername,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public sealed record ResolveUserReportRequest(string Status, string? ResolutionNote);

public sealed record ManagedUserListResponse(IReadOnlyList<ManagedUserDto> Items);
public sealed record ManagedUserDto(
    Guid Id,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles,
    int TotalXp,
    bool IsSuspended,
    string? SuspensionReason,
    DateTimeOffset? SuspendedAtUtc,
    DateTimeOffset RegisteredAtUtc);

public sealed record SetUserSuspensionRequest(bool Suspend, string? Reason);
public sealed record UpdateUserRoleRequest(string Role, bool Grant);

public sealed record AdminAuditListResponse(IReadOnlyList<AdminAuditEntryDto> Items);
public sealed record AdminAuditEntryDto(
    long Id,
    Guid? ActorUserId,
    string ActorUsername,
    string ActionType,
    string TargetKind,
    string TargetIdentifier,
    string Summary,
    DateTimeOffset CreatedAtUtc);
