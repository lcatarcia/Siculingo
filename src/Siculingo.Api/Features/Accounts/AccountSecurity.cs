using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Siculingo.Domain.Administration;
using Siculingo.Infrastructure.Accounts;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Features.Accounts;

public static class AccountSecurity
{
    public static IServiceCollection AddAccounts(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SiculingoDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminRoles.EditorialPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireRole(AdminRoles.Editor, AdminRoles.Admin))
            .AddPolicy(AdminRoles.AdminPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireRole(AdminRoles.Admin));
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        var local = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = local ? "Siculingo.Auth" : "__Host-Siculingo.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                await SecurityStampValidator.ValidatePrincipalAsync(context);
                if (context.Principal is not null)
                {
                    var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                    var currentUser = await userManager.GetUserAsync(context.Principal);
                    if (currentUser is null || currentUser.IsSuspended)
                    {
                        context.RejectPrincipal();
                        return;
                    }
                }
                // Validate revocation on every request without renewing the absolute expiry.
                context.ShouldRenew = false;
            };
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = local ? "Siculingo.Csrf" : "__Host-Siculingo.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("credentials", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }
                await Results.Problem(statusCode: 429, title: "Troppi tentativi",
                    detail: "Attendi un minuto prima di riprovare.").ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}

public sealed class AccountAntiforgeryFilter(
    IAntiforgery antiforgery, ILogger<AccountAntiforgeryFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                logger.LogWarning("Rejected account request with invalid antiforgery token.");
                return TypedResults.Problem(statusCode: 403, title: "Richiesta non verificata",
                    detail: "Ricarica la pagina e riprova.");
            }
        }
        return await next(context);
    }
}
