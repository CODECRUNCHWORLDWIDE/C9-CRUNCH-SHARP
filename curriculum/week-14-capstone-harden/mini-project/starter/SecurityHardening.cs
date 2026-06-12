// Polyglot Workshop — Security hardening scaffolding (starter)
//
// Rate limiting (OWASP API4), security headers + HTTPS (API8), the SSRF host guard
// (API7), and the hardened JWT bearer token validation (API2). Compose these into
// Program.cs; fill in the TODOs where a workshop-specific decision is yours.
//
// Citations:
//   Rate limiting: https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
//   JWT bearer:    https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication
//   SSRF:          https://owasp.org/API-Security/editions/2023/en/0xa7-server-side-request-forgery/

#nullable enable
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Workshop.Api.Security;

// ---- Rate limiting (API4) ----------------------------------------------------

public static class RateLimitingSetup
{
    public const int MaxPageSize = 100;   // pagination cap; clamp every list endpoint

    public static IServiceCollection AddWorkshopRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "1";
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "rate_limited", retryAfterSeconds = 1 }, ct);
            };

            // Per-authenticated-user token bucket: one noisy tenant cannot starve others.
            options.AddPolicy("per-user", httpContext =>
            {
                string key = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? httpContext.Connection.RemoteIpAddress?.ToString()
                             ?? "anonymous";
                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 100,
                    TokensPerPeriod = 20,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });

            // TODO(you): add a tighter "sensitive-flow" policy (API6) for enroll /
            // submit-on-behalf / bulk-grade, e.g. 10 requests/minute per user.
        });
        return services;
    }
}

// ---- Hardened JWT bearer (API2) ---------------------------------------------

public static class AuthenticationSetup
{
    public static IServiceCollection AddWorkshopAuthentication(
        this IServiceCollection services, IConfiguration cfg)
    {
        string authority = cfg["Oidc:Authority"]
            ?? throw new InvalidOperationException("Oidc:Authority required (Keycloak realm URL).");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = authority;
                o.Audience  = cfg["Oidc:Audience"] ?? "workshop-api";
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    // Default skew is 5 min; tighten it — long-lived skew widens replay windows.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // SignalR carve-out: accept the access_token query param ONLY for /hubs/*.
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) &&
                            ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            ctx.Token = token;
                        }
                        return Task.CompletedTask;
                    }
                };
            });
        return services;
    }
}

// ---- Security headers + HTTPS (API8) ----------------------------------------

public static class SecurityHeadersMiddleware
{
    public static IApplicationBuilder UseWorkshopSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            // CSP is tuned for the Blazor admin in its own project; this is the API default.
            h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            await next();
        });
}

// ---- SSRF host guard (API7) --------------------------------------------------

public static class SsrfGuard
{
    // The lesson-import feature is the only outbound-fetch surface. Block loopback,
    // link-local (cloud metadata), and private ranges; never fetch a raw user URL.
    public static bool IsAllowedImportHost(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (!IPAddress.TryParse(uri.Host, out var ip))
        {
            // A hostname: resolve and re-check, and pin to an allow-list of known hosts.
            // TODO(you): resolve DNS and re-validate the resulting IP to defeat DNS rebinding.
            return AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        }

        return !IsBlockedAddress(ip);
    }

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "raw.githubusercontent.com", "gist.githubusercontent.com"
    };

    private static bool IsBlockedAddress(IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        if (IPAddress.IsLoopback(ip)) return true;                 // 127.0.0.0/8, ::1
        if (b.Length == 4)
        {
            if (b[0] == 10) return true;                           // 10.0.0.0/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;           // 192.168.0.0/16
            if (b[0] == 169 && b[1] == 254) return true;           // 169.254.0.0/16 (metadata!)
        }
        return false;
    }
}
