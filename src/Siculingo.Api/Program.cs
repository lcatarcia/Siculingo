using Siculingo.Api.Common;
using Siculingo.Api.Common.Errors;
using Siculingo.Api.Features.System;
using Siculingo.Infrastructure.Persistence;
using Siculingo.Api.Features.Accounts;
using Siculingo.Api.Features.Administration;
using Siculingo.Api.Features.Gamification;
using Siculingo.Api.Features.Learning;
using Siculingo.Api.Features.Social;
using Siculingo.Infrastructure.Editorial;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<HeartsOptions>(builder.Configuration.GetSection(HeartsOptions.SectionName));
builder.Services.Configure<LegalOptions>(builder.Configuration.GetSection(LegalOptions.SectionName));
builder.Services.AddProductionHosting(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddAccounts(builder.Environment);

var app = builder.Build();

if (await OperationalCli.TryExecuteAsync(app, builder.Configuration, args))
{
    return;
}

app.UseConfiguredForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSpaStaticFilesIfPresent();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
}
app.MapSystemStatus();
app.MapServiceHealth();
app.MapAccounts();
app.MapLearningPath();
app.MapLessonAttempts();
app.MapGamification();
app.MapSocial();
app.MapAdministration();
app.MapSpaFallbackIfPresent();
app.Run();

public partial class Program;
