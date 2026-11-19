using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siculingo.Api.Common;
using Siculingo.Infrastructure.Accounts;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Accounts;

public static partial class AccountEndpoints
{
    public static void MapAccounts(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Accounts")
            .WithDescription("I POST richiedono X-CSRF-TOKEN e il cookie antiforgery, ottenuti con GET /api/v1/auth/csrf. Dopo un cambio di identità richiedere un nuovo token. Nessuna password o credenziale di sessione va conservata in localStorage.")
            .AddEndpointFilter<AccountAntiforgeryFilter>();
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            TypedResults.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(context).RequestToken
                ?? throw new InvalidOperationException("Antiforgery token was not generated."))))
            .WithName("GetCsrfToken");
        group.MapGet("/me", GetCurrentAccount).RequireAuthorization()
            .WithName("GetCurrentAccount").ProducesProblem(401);
        group.MapPost("/register", Register).WithName("RegisterAccount")
            .RequireRateLimiting("credentials").ProducesProblem(400).ProducesProblem(403)
            .ProducesProblem(409).ProducesProblem(429);
        group.MapPost("/login", Login).WithName("LoginAccount")
            .RequireRateLimiting("credentials").ProducesProblem(401).ProducesProblem(403).ProducesProblem(429);
        group.MapPost("/recover", Recover).WithName("RecoverAccount")
            .RequireRateLimiting("credentials").ProducesProblem(400).ProducesProblem(403).ProducesProblem(429);
        group.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireAuthorization().WithName("LogoutAccount").ProducesProblem(401).ProducesProblem(403);
        group.MapPost("/logout-all", LogoutAll).RequireAuthorization()
            .WithName("LogoutAllDevices").ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        group.MapPost("/profile", UpdateProfile).RequireAuthorization()
            .WithName("UpdateAccountProfile").ProducesProblem(400).ProducesProblem(401)
            .ProducesProblem(403).ProducesProblem(409);
        group.MapPost("/password", ChangePassword).RequireAuthorization()
            .RequireRateLimiting("credentials").WithName("ChangeAccountPassword")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(429);
        group.MapPost("/delete", DeleteAccount).RequireAuthorization()
            .RequireRateLimiting("credentials").WithName("DeleteAccount")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(429);
    }

    private static async Task<Results<Ok<AccountResponse>, UnauthorizedHttpResult>> GetCurrentAccount(
        HttpContext context, UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is null || user.IsSuspended) return TypedResults.Unauthorized();
        return TypedResults.Ok(await ToResponseAsync(user, users));
    }

    private static async Task<Results<Ok<RecoveryResponse>, ProblemHttpResult>> Register(
        RegisterAccountRequest request, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, SiculingoDbContext db,
        TimeProvider clock, IOptions<LegalOptions> legalOptions)
    {
        var validation = ValidateCredentials(request.Username, request.Password);
        if (validation is not null) return InvalidInput(validation);
        if (!request.AcceptTerms)
            return InvalidInput("Per creare un account devi confermare la presa visione dell'informativa privacy e dei termini della beta.");

        var now = clock.GetUtcNow();
        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            RegisteredAtUtc = now,
            AcceptedTermsAtUtc = now,
            AcceptedTermsVersion = legalOptions.Value.TermsVersion
        };
        var code = NewRecoveryCode();
        await using var transaction = await db.Database.BeginTransactionAsync();
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return TypedResults.Problem(statusCode: 409, title: "Nome utente non disponibile",
                detail: "Scegli un altro nome utente.");
        }
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
                return TypedResults.Problem(statusCode: 409, title: "Nome utente non disponibile",
                    detail: "Scegli un altro nome utente.");
            return InvalidInput("Controlla nome utente e password.");
        }
        db.AccountRecoveryCodes.Add(new AccountRecoveryCode { UserId = user.Id, CodeHash = HashCode(code) });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.Ok(new RecoveryResponse(ToResponse(user, []), code));
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> Login(
        CredentialsRequest request, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn)
    {
        if (request.Username is not null && request.Username.Trim().Length is >= 3 and <= 24
            && request.Password is not null && request.Password.Length is >= 1 and <= 128)
        {
            var user = await users.FindByNameAsync(request.Username.Trim());
            if (user is not null)
            {
                if (user.IsSuspended)
                {
                    return TypedResults.Problem(statusCode: 403, title: "Account sospeso",
                        detail: "Questo account è attualmente sospeso e non può effettuare l'accesso.");
                }

                var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    await signIn.SignInAsync(user, isPersistent: true);
                    return TypedResults.Ok(await ToResponseAsync(user, users));
                }
            }
        }
        return TypedResults.Problem(statusCode: 401, title: "Accesso non riuscito",
            detail: "Nome utente o password non validi, oppure accesso temporaneamente bloccato. Riprova più tardi.");
    }

    private static async Task<Results<Ok<RecoveryResponse>, ProblemHttpResult>> Recover(
        RecoveryRequest request, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, SiculingoDbContext db)
    {
        var validation = ValidateCredentials(request.Username, request.NewPassword);
        if (validation is not null) return InvalidInput(validation);
        if (string.IsNullOrWhiteSpace(request.RecoveryCode) || request.RecoveryCode.Length > 128)
            return InvalidRecovery();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByNameAsync(request.Username.Trim());
        if (user is null || user.IsSuspended) return InvalidRecovery();
        var stored = await db.AccountRecoveryCodes.SingleOrDefaultAsync(code => code.UserId == user.Id);
        if (stored is null || !CryptographicOperations.FixedTimeEquals(stored.CodeHash, HashCode(request.RecoveryCode.Trim())))
            return InvalidRecovery();

        var newCode = NewRecoveryCode();
        stored.CodeHash = HashCode(newCode);
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!reset.Succeeded) return InvalidRecovery();
        await transaction.CommitAsync();
        await signIn.SignOutAsync();
        return TypedResults.Ok(new RecoveryResponse(await ToResponseAsync(user, users), newCode));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> LogoutAll(
        HttpContext context, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is null) return TypedResults.Unauthorized();
        var result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
            return TypedResults.Problem(statusCode: 409, title: "Revoca non completata", detail: "Riprova l'operazione.");
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AccountResponse>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateProfile(
        ProfileRequest request, HttpContext context, UserManager<ApplicationUser> users)
    {
        var displayName = request.DisplayName?.Trim();
        if (displayName is null || displayName.Length > 40 || displayName.Any(char.IsControl))
            return InvalidInput("Il nome visualizzato deve avere al massimo 40 caratteri, senza caratteri di controllo.");
        var user = await users.GetUserAsync(context.User);
        if (user is null) return TypedResults.Unauthorized();
        user.DisplayName = displayName.Length == 0 ? null : displayName;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
            return TypedResults.Problem(statusCode: 409, title: "Profilo non aggiornato",
                detail: "I dati sono cambiati. Ricarica la pagina e riprova.");
        return TypedResults.Ok(await ToResponseAsync(user, users));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> ChangePassword(
        ChangePasswordRequest request, HttpContext context,
        UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn)
    {
        if (request.CurrentPassword is null || request.CurrentPassword.Length is < 1 or > 128
            || request.NewPassword is null || request.NewPassword.Length is < 12 or > 128)
            return InvalidInput("Inserisci la password attuale e una nuova password da 12 a 128 caratteri.");
        if (request.CurrentPassword == request.NewPassword)
            return InvalidInput("Scegli una password diversa da quella attuale.");
        var user = await users.GetUserAsync(context.User);
        if (user is null) return TypedResults.Unauthorized();
        var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
        if (!check.Succeeded)
            return InvalidInput("Password attuale non valida oppure operazione temporaneamente bloccata. Riprova più tardi.");
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return TypedResults.Problem(statusCode: 409, title: "Password non aggiornata",
                detail: "I dati sono cambiati. Ricarica la pagina e riprova.");
        // A fresh login starts the next absolute 30-day session; no cookie is renewed here.
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAccount(
        DeleteAccountRequest request,
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        SiculingoDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 128)
            return InvalidInput("Inserisci la password attuale per confermare l'eliminazione definitiva dell'account.");

        var user = await users.GetUserAsync(context.User);
        if (user is null) return TypedResults.Unauthorized();

        var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
        if (!check.Succeeded)
            return InvalidInput("Password attuale non valida oppure operazione temporaneamente bloccata. Riprova più tardi.");

        var userId = user.Id;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Anonymize historical moderation, editorial and operational references
        await db.UserReports.Where(r => r.ReporterUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReporterUserId, (Guid?)null), cancellationToken);
        await db.UserReports.Where(r => r.TargetUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.TargetUserId, (Guid?)null), cancellationToken);
        await db.UserReports.Where(r => r.ResolvedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ResolvedByUserId, (Guid?)null), cancellationToken);

        await db.AdminAuditEntries.Where(a => a.ActorUserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.ActorUserId, (Guid?)null)
                .SetProperty(a => a.ActorUsername, "[account-eliminato]"), cancellationToken);

        await db.Lessons.Where(l => l.LastEditedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastEditedByUserId, (Guid?)null), cancellationToken);
        await db.Lessons.Where(l => l.ApprovedByUserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.ApprovedByUserId, (Guid?)null)
                .SetProperty(l => l.ApprovedByName, "[account-eliminato]"), cancellationToken);

        await db.LinguisticLexemes.Where(l => l.LastEditedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastEditedByUserId, (Guid?)null), cancellationToken);
        await db.LinguisticLexemes.Where(l => l.ReviewedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ReviewedByUserId, (Guid?)null), cancellationToken);

        await db.Users.Where(u => u.SuspendedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedByUserId, (Guid?)null), cancellationToken);

        // Remove social and gamification rows in strict foreign-key order
        await db.Friendships.Where(f => f.UserLowId == userId || f.UserHighId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.UserBlocks.Where(b => b.BlockerUserId == userId || b.BlockedUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await db.UserMissionProgress.Where(m => m.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.UserAchievements.Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.XpLedger.Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var attemptIdsQuery = db.LessonAttempts.Where(a => a.UserId == userId).Select(a => a.Id);
        await db.LessonAttemptAnswers.Where(ans => attemptIdsQuery.Contains(ans.LessonAttemptId))
            .ExecuteDeleteAsync(cancellationToken);
        await db.LessonAttempts.Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await db.UserLessonCompletions.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UserStats.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UserStreaks.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UserHearts.Where(h => h.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UserPrivacySettings.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UserLeagueProfiles.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.WeeklyLeagueStandings.Where(w => w.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.AccountRecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        db.ChangeTracker.Clear();
        var freshUser = await users.FindByIdAsync(userId.ToString());
        if (freshUser is not null)
        {
            var deleteResult = await users.DeleteAsync(freshUser);
            if (!deleteResult.Succeeded)
                return TypedResults.Problem(statusCode: 409, title: "Eliminazione non completata",
                    detail: "Impossibile completare la cancellazione dell'account. Riprova tra qualche istante.");
        }

        await transaction.CommitAsync(cancellationToken);
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static string? ValidateCredentials(string? username, string? password)
    {
        if (username is null || !UsernamePattern().IsMatch(username.Trim()))
            return "Il nome utente deve avere da 3 a 24 caratteri: lettere senza accenti, numeri o underscore.";
        if (password is null || password.Length is < 12 or > 128)
            return "La password deve avere da 12 a 128 caratteri. Puoi usare una frase facile da ricordare.";
        return null;
    }

    private static ProblemHttpResult InvalidInput(string detail) =>
        TypedResults.Problem(statusCode: 400, title: "Controlla i dati inseriti", detail: detail);

    private static ProblemHttpResult InvalidRecovery() => TypedResults.Problem(statusCode: 400,
        title: "Recupero non riuscito", detail: "Nome utente o codice non validi, oppure codice già utilizzato.");

    private static async Task<AccountResponse> ToResponseAsync(ApplicationUser user, UserManager<ApplicationUser> users)
    {
        var roles = await users.GetRolesAsync(user);
        return ToResponse(user, roles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static AccountResponse ToResponse(ApplicationUser user, IReadOnlyList<string> roles) =>
        new(user.Id, user.UserName ?? throw new InvalidOperationException("Account has no username."),
            user.DisplayName ?? user.UserName ?? throw new InvalidOperationException("Account has no username."),
            roles);
    private static string NewRecoveryCode() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static byte[] HashCode(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

    [GeneratedRegex("^[A-Za-z0-9_]{3,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}

public sealed record RegisterAccountRequest(string Username, string Password, bool AcceptTerms = false);
public sealed record CredentialsRequest(string Username, string Password);
public sealed record RecoveryRequest(string Username, string RecoveryCode, string NewPassword);
public sealed record AccountResponse(Guid Id, string Username, string DisplayName, IReadOnlyList<string>? Roles = null);
public sealed record ProfileRequest(string DisplayName);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record DeleteAccountRequest(string CurrentPassword);
public sealed record RecoveryResponse(AccountResponse Account, string RecoveryCode);
public sealed record CsrfResponse(string Token);
