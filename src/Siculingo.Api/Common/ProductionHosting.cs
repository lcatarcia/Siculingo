using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace Siculingo.Api.Common;

public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>
    /// Enables X-Forwarded-For and X-Forwarded-Proto processing behind a trusted reverse proxy
    /// (for example Azure App Service front-end TLS termination). Keep false when exposed directly.
    /// </summary>
    public bool EnableForwardedHeaders { get; set; }

    /// <summary>
    /// Maximum reverse-proxy hops processed from right to left (1 for Azure App Service).
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// Optional explicit reverse-proxy IP addresses. When empty and EnableForwardedHeaders is true,
    /// default loopback restrictions are cleared so managed cloud load balancers work with ForwardLimit.
    /// </summary>
    public string[] TrustedProxyAddresses { get; set; } = [];
}

public sealed class DataProtectionStorageOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Persistent directory for ASP.NET Core Data Protection keys (e.g. /home/site/dataprotection-keys on Azure App Service).
    /// </summary>
    public string? KeysDirectory { get; set; }
}

public static partial class ProductionHosting
{
    [GeneratedRegex(@"-[A-Za-z0-9_-]{8,}\.(js|css|woff2|png|webp|svg)$", RegexOptions.CultureInvariant)]
    private static partial Regex HashedBundleAssetRegex();

    public static IServiceCollection AddProductionHosting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HostingOptions>(configuration.GetSection(HostingOptions.SectionName));
        services.Configure<DataProtectionStorageOptions>(configuration.GetSection(DataProtectionStorageOptions.SectionName));

        services.AddDataProtection()
            .SetApplicationName("Siculingo");

        services.AddOptions<KeyManagementOptions>()
            .Configure<IConfiguration, ILoggerFactory>((keyOptions, cfg, loggerFactory) =>
            {
                var resolvedKeysDir = ResolveKeysDirectory(cfg);
                if (!string.IsNullOrWhiteSpace(resolvedKeysDir))
                {
                    Directory.CreateDirectory(resolvedKeysDir);
                    keyOptions.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(resolvedKeysDir), loggerFactory);
                }
            });

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<HostingOptions>>((options, hostingOptions) =>
            {
                var hosting = hostingOptions.Value;
                if (!hosting.EnableForwardedHeaders)
                {
                    return;
                }

                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = Math.Max(1, hosting.ForwardLimit);

                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();

                foreach (var rawIp in hosting.TrustedProxyAddresses)
                {
                    if (IPAddress.TryParse(rawIp, out var ip))
                    {
                        options.KnownProxies.Add(ip);
                    }
                }
            });

        return services;
    }

    public static void UseConfiguredForwardedHeaders(this WebApplication app)
    {
        var hosting = app.Configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>();
        if (hosting?.EnableForwardedHeaders == true)
        {
            app.UseForwardedHeaders();
        }

        var resolvedKeysDir = ResolveKeysDirectory(app.Configuration);
        if (!app.Environment.IsDevelopment()
            && !app.Environment.IsEnvironment("Testing")
            && string.IsNullOrWhiteSpace(resolvedKeysDir))
        {
            app.Logger.LogWarning(
                "DataProtection:KeysDirectory is not configured in {Environment}. Session cookies will not survive process restarts.",
                app.Environment.EnvironmentName);
        }
    }

    public static void UseSpaStaticFilesIfPresent(this WebApplication app)
    {
        var webRoot = GetEffectiveWebRoot(app.Environment);
        if (!Directory.Exists(webRoot))
        {
            return;
        }

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = app.Environment.WebRootFileProvider
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = contentTypeProvider,
            OnPrepareResponse = context =>
            {
                var requestPath = context.Context.Request.Path.Value ?? string.Empty;
                var fileName = context.File.Name;

                if (string.Equals(fileName, "index.html", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "sw.js", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "no-cache";
                }
                else if (requestPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
                         && HashedBundleAssetRegex().IsMatch(fileName))
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                }
                else if (requestPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=86400";
                }
            }
        });
    }

    public static void MapSpaFallbackIfPresent(this WebApplication app)
    {
        var indexPath = Path.Combine(GetEffectiveWebRoot(app.Environment), "index.html");
        if (!File.Exists(indexPath))
        {
            return;
        }

        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                || path.StartsWithSegments("/api")
                || path.StartsWithSegments("/health")
                || path.StartsWithSegments("/openapi")
                || Path.HasExtension(path.Value))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                if (path.StartsWithSegments("/api"))
                {
                    await Results.Problem(
                            statusCode: StatusCodes.Status404NotFound,
                            title: "Risorsa non trovata",
                            detail: "L'endpoint API richiesto non esiste.")
                        .ExecuteAsync(context);
                }
                return;
            }

            context.Response.Headers.CacheControl = "no-cache";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(indexPath, context.RequestAborted);
        });
    }

    public static string? ResolveKeysDirectory(IConfiguration configuration)
    {
        var configured = configuration[$"{DataProtectionStorageOptions.SectionName}:{nameof(DataProtectionStorageOptions.KeysDirectory)}"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        // Default persistent storage path on Azure App Service Linux (/home is backed by persistent storage).
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"))
            && Directory.Exists("/home"))
        {
            return "/home/site/dataprotection-keys";
        }

        return null;
    }

    private static string GetEffectiveWebRoot(IWebHostEnvironment environment) =>
        !string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? environment.WebRootPath
            : Path.Combine(environment.ContentRootPath, "wwwroot");
}
