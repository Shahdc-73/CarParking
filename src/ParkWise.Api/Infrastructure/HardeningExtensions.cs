using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ParkWise.Api.Infrastructure;

/// <summary>Request-level protections: who the client really is behind a proxy, brute-force limits, and response headers.</summary>
public static class HardeningExtensions
{
    public const string AuthPolicy = "auth";

    /// <summary>
    /// Behind nginx the TCP peer is the proxy, so the real client IP is only in X-Forwarded-For. That header is trusted ONLY when
    /// the peer is in <c>ForwardedHeaders:KnownNetworks</c> (CIDR list); anyone else could simply write any IP into it.
    /// </summary>
    public static IServiceCollection AddForwardedHeadersFromConfig(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var cidr in config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
        });
        return services;
    }

    /// <summary>Fixed window per client IP for the unauthenticated auth endpoints (login, register, refresh).</summary>
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var permit = config.GetValue("RateLimiting:Auth:PermitLimit", 10);
        var window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:Auth:WindowSeconds", 60));

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = window, QueueLimit = 0 }));

            o.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                response.StatusCode = StatusCodes.Status429TooManyRequests;
                var problem = new ProblemDetails { Status = 429, Title = "Too many requests. Please wait a moment and try again." };
                problem.Extensions["code"] = "rate_limit.exceeded";
                await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", ct);
            };
        });
        return services;
    }

    /// <summary>Headers the API itself should send (nginx only covers the web app): no sniffing, no referrer, no caching of auth responses.</summary>
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "no-referrer";
            if (context.Request.Path.StartsWithSegments("/api/v1/auth"))
                h.CacheControl = "no-store"; // responses hold access tokens
            return Task.CompletedTask;
        });
        return next();
    });
}
