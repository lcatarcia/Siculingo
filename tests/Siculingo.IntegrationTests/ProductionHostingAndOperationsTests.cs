using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Siculingo.Api.Features.Accounts;
using Xunit;

namespace Siculingo.IntegrationTests;

public sealed class ProductionHostingAndOperationsTests
{
    private const string Password = "frase_sicura_catanese_99";

    [Fact]
    public async Task Production_spa_hosting_serves_wwwroot_cache_headers_and_returns_problem_json_for_unknown_api_routes()
    {
        var tempWebRoot = Path.Combine(Path.GetTempPath(), $"siculingo-wwwroot-{Guid.NewGuid():N}");
        var tempKeysDir = Path.Combine(Path.GetTempPath(), $"siculingo-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempWebRoot, "assets", "mascot"));

        await File.WriteAllTextAsync(Path.Combine(tempWebRoot, "index.html"), "<!doctype html><title>Siculingo Prod</title><div id=\"root\"></div>");
        await File.WriteAllTextAsync(Path.Combine(tempWebRoot, "sw.js"), "self.addEventListener('fetch', () => {});");
        await File.WriteAllTextAsync(Path.Combine(tempWebRoot, "manifest.webmanifest"), "{\"name\":\"Siculingo\"}");
        await File.WriteAllTextAsync(Path.Combine(tempWebRoot, "assets", "index-CM8aIZ_w.js"), "console.log('bundle');");
        await File.WriteAllBytesAsync(Path.Combine(tempWebRoot, "assets", "mascot", "ficodi.png"), [137, 80, 78, 71]);

        await using var prodFactory = new DatabaseApiFactory(
            extraConfiguration: new Dictionary<string, string?>
            {
                ["Hosting:EnableForwardedHeaders"] = "true",
                ["Hosting:ForwardLimit"] = "1",
                ["DataProtection:KeysDirectory"] = tempKeysDir
            },
            environmentName: "Production",
            webRootPath: tempWebRoot);

        try
        {
            await using (var db = prodFactory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
            }

            using var client = prodFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://siculingo-beta.azurewebsites.net"),
                AllowAutoRedirect = false
            });
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

            // 1. Root "/" and client-side SPA routes ("/impara") serve index.html with no-cache + HSTS
            using (var rootResp = await client.GetAsync("/"))
            {
                Assert.Equal(HttpStatusCode.OK, rootResp.StatusCode);
                Assert.Contains("Siculingo Prod", await rootResp.Content.ReadAsStringAsync());
                Assert.Contains("no-cache", rootResp.Headers.CacheControl?.ToString() ?? string.Empty);
                Assert.True(rootResp.Headers.Contains("Strict-Transport-Security"));
            }

            using (var spaRouteResp = await client.GetAsync("/impara"))
            {
                Assert.Equal(HttpStatusCode.OK, spaRouteResp.StatusCode);
                Assert.Contains("Siculingo Prod", await spaRouteResp.Content.ReadAsStringAsync());
                Assert.Contains("no-cache", spaRouteResp.Headers.CacheControl?.ToString() ?? string.Empty);
            }

            // 2. Web App Manifest and Service Worker use proper MIME type and no-cache revalidation
            using (var manifestResp = await client.GetAsync("/manifest.webmanifest"))
            {
                Assert.Equal(HttpStatusCode.OK, manifestResp.StatusCode);
                Assert.Equal("application/manifest+json", manifestResp.Content.Headers.ContentType?.MediaType);
                Assert.Contains("no-cache", manifestResp.Headers.CacheControl?.ToString() ?? string.Empty);
            }

            using (var swResp = await client.GetAsync("/sw.js"))
            {
                Assert.Equal(HttpStatusCode.OK, swResp.StatusCode);
                Assert.Contains("no-cache", swResp.Headers.CacheControl?.ToString() ?? string.Empty);
            }

            // 3. Vite hashed assets are immutable, while unhashed static kit assets have bounded max-age
            using (var hashedAssetResp = await client.GetAsync("/assets/index-CM8aIZ_w.js"))
            {
                Assert.Equal(HttpStatusCode.OK, hashedAssetResp.StatusCode);
                var cache = hashedAssetResp.Headers.CacheControl?.ToString() ?? string.Empty;
                Assert.Contains("immutable", cache);
                Assert.Contains("31536000", cache);
            }

            using (var kitAssetResp = await client.GetAsync("/assets/mascot/ficodi.png"))
            {
                Assert.Equal(HttpStatusCode.OK, kitAssetResp.StatusCode);
                var cache = kitAssetResp.Headers.CacheControl?.ToString() ?? string.Empty;
                Assert.Contains("86400", cache);
            }

            // 4. Unknown /api/* routes return 404 ProblemDetails JSON (never SPA index.html), and OpenAPI is disabled in Production
            using (var unknownApiResp = await client.GetAsync("/api/v1/nonexistent-endpoint"))
            {
                Assert.Equal(HttpStatusCode.NotFound, unknownApiResp.StatusCode);
                Assert.Equal("application/problem+json", unknownApiResp.Content.Headers.ContentType?.MediaType);
            }

            using (var missingStaticResp = await client.GetAsync("/assets/does-not-exist.js"))
            {
                Assert.Equal(HttpStatusCode.NotFound, missingStaticResp.StatusCode);
            }

            using (var openApiResp = await client.GetAsync("/openapi/v1.json"))
            {
                Assert.Equal(HttpStatusCode.NotFound, openApiResp.StatusCode);
            }
        }
        finally
        {
            await prodFactory.DeleteTestDatabaseAsync();
            if (Directory.Exists(tempWebRoot))
                Directory.Delete(tempWebRoot, recursive: true);
            if (Directory.Exists(tempKeysDir))
                Directory.Delete(tempKeysDir, recursive: true);
        }
    }

    [Fact]
    public async Task Production_host_cookies_forwarded_ip_spoofing_protection_and_dataprotection_keyring_survive_restart()
    {
        var tempKeysDir = Path.Combine(Path.GetTempPath(), $"siculingo-dpkeys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempKeysDir);

        var prodConfig = new Dictionary<string, string?>
        {
            ["Hosting:EnableForwardedHeaders"] = "true",
            ["Hosting:ForwardLimit"] = "1",
            ["DataProtection:KeysDirectory"] = tempKeysDir
        };

        string sharedConnectionString;
        string? capturedHostAuthCookieHeader = null;

        var factoryA = new DatabaseApiFactory(
            extraConfiguration: prodConfig,
            environmentName: "Production");
        sharedConnectionString = factoryA.ConnectionString;

        try
        {
            await using (var db = factoryA.CreateDbContext())
            {
                await db.Database.MigrateAsync();
            }

            await using (factoryA)
            {
                using var clientA = factoryA.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://siculingo-beta.azurewebsites.net"),
                    AllowAutoRedirect = false
                });
                clientA.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "10.0.0.1");
                clientA.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
                clientA.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.10");

                // Verify __Host- CSRF cookie is Secure + HttpOnly in Production
                string csrfToken;
                using (var csrfResp = await clientA.GetAsync("/api/v1/auth/csrf"))
                {
                    Assert.Equal(HttpStatusCode.OK, csrfResp.StatusCode);
                    var setCookies = csrfResp.Headers.GetValues("Set-Cookie").ToArray();
                    Assert.Contains(setCookies, c =>
                        c.StartsWith("__Host-Siculingo.Csrf=", StringComparison.Ordinal)
                        && c.Contains("secure", StringComparison.OrdinalIgnoreCase)
                        && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

                    var body = await csrfResp.Content.ReadFromJsonAsync<CsrfResponse>();
                    csrfToken = body!.Token;
                }

                // Register user and capture raw __Host-Siculingo.Auth cookie
                using (var regReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
                {
                    Content = JsonContent.Create(new RegisterAccountRequest("utente_prod", Password, AcceptTerms: true))
                })
                {
                    regReq.Headers.Add("X-CSRF-TOKEN", csrfToken);
                    using var regResp = await clientA.SendAsync(regReq);
                    Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);

                    var setCookies = regResp.Headers.GetValues("Set-Cookie").ToArray();
                    var authSetCookie = Assert.Single(setCookies, c => c.StartsWith("__Host-Siculingo.Auth=", StringComparison.Ordinal));
                    Assert.Contains("secure", authSetCookie, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("httponly", authSetCookie, StringComparison.OrdinalIgnoreCase);
                    capturedHostAuthCookieHeader = authSetCookie.Split(';', 2)[0];
                }

                // Verify ForwardLimit=1 ignores client-spoofed left X-Forwarded-For entry ("203.0.113.99, 198.51.100.42" -> real hop is 198.51.100.42)
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    using var rateClient = factoryA.CreateClient(new WebApplicationFactoryClientOptions
                    {
                        BaseAddress = new Uri("https://siculingo-beta.azurewebsites.net"),
                        AllowAutoRedirect = false
                    });
                    rateClient.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "10.0.0.1");
                    rateClient.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
                    rateClient.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{attempt}, 198.51.100.42");

                    var tok = (await rateClient.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!.Token;
                    using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                    {
                        Content = JsonContent.Create(new CredentialsRequest("inesistente_ip", Password))
                    };
                    loginReq.Headers.Add("X-CSRF-TOKEN", tok);
                    using var loginResp = await rateClient.SendAsync(loginReq);
                    Assert.Equal(HttpStatusCode.Unauthorized, loginResp.StatusCode);
                }

                // 21st attempt from real proxy hop 198.51.100.42 is throttled (429), even if attacker rotates spoofed prefix!
                using (var throttledClient = factoryA.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://siculingo-beta.azurewebsites.net"),
                    AllowAutoRedirect = false
                }))
                {
                    throttledClient.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "10.0.0.1");
                    throttledClient.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
                    throttledClient.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.250, 198.51.100.42");
                    var tok = (await throttledClient.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!.Token;
                    using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                    {
                        Content = JsonContent.Create(new CredentialsRequest("inesistente_ip", Password))
                    };
                    loginReq.Headers.Add("X-CSRF-TOKEN", tok);
                    using var loginResp = await throttledClient.SendAsync(loginReq);
                    Assert.Equal(HttpStatusCode.TooManyRequests, loginResp.StatusCode);
                }
            }

            // Host A is now completely disposed. Boot a fresh Host B pointing to the same DB & DataProtection KeysDirectory!
            Assert.NotEmpty(Directory.GetFiles(tempKeysDir, "*.xml"));

            await using var factoryB = new DatabaseApiFactory(
                extraConfiguration: prodConfig,
                environmentName: "Production",
                existingConnectionString: sharedConnectionString);

            using var clientB = factoryB.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://siculingo-beta.azurewebsites.net"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });
            clientB.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "10.0.0.1");
            clientB.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            clientB.DefaultRequestHeaders.Add("Cookie", capturedHostAuthCookieHeader!);

            var currentAccount = await clientB.GetFromJsonAsync<AccountResponse>("/api/v1/auth/me");
            Assert.NotNull(currentAccount);
            Assert.Equal("utente_prod", currentAccount!.Username);
        }
        finally
        {
            await using var cleanupFactory = new DatabaseApiFactory(existingConnectionString: sharedConnectionString);
            await cleanupFactory.DeleteTestDatabaseAsync();
            if (Directory.Exists(tempKeysDir))
                Directory.Delete(tempKeysDir, recursive: true);
        }
    }
}
