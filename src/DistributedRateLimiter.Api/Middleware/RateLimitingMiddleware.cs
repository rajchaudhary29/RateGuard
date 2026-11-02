using System.Text.Json;
using System.Text.RegularExpressions;
using DistributedRateLimiter.Api.Models;
using DistributedRateLimiter.Api.Models.Entities;
using DistributedRateLimiter.Api.Options;
using DistributedRateLimiter.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DistributedRateLimiter.Api.Middleware;

/// <summary>
/// High-performance ASP.NET Core middleware enforcing distributed sliding-window rate limiting.
/// Returns RFC 7807 compliant ProblemDetails and standard rate-limiting headers.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly RateLimitOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RateLimitingMiddleware(
        RequestDelegate next,
        IOptions<RateLimitOptions> options,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IClientIdentifierResolver identityResolver,
        IRateLimiterService rateLimiterService,
        IAuditLogService auditLogService)
    {
        var path = context.Request.Path.Value ?? "/";

        // Bypass swagger, OpenAPI, and health-check endpoints from rate limits
        if (ShouldBypass(path))
        {
            await _next(context);
            return;
        }

        // 1. Resolve client identity and tier
        var client = identityResolver.ResolveClientIdentity(context);

        // 2. Resolve matching rule for path and method
        var rule = ResolveMatchingRule(path, context.Request.Method, client.Tier);

        // 3. Evaluate distributed rate limit in Redis
        var result = await rateLimiterService.CheckRateLimitAsync(client.ClientKey, rule, context.RequestAborted);

        // 4. Inject standard rate limit headers
        context.Response.Headers["X-RateLimit-Limit"] = rule.PermitLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = result.ResetTimeUtc.ToUnixTimeSeconds().ToString();

        // 5. Handle rate limit exceeded
        if (!result.IsAllowed)
        {
            context.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/problem+json";

            // Enqueue audit log asynchronously to PostgreSQL buffer
            auditLogService.LogViolation(new RateLimitViolationLog
            {
                ClientKey = client.ClientKey,
                ClientIp = client.ClientIp,
                Tier = client.Tier,
                RequestPath = path,
                HttpMethod = context.Request.Method,
                StatusCode = StatusCodes.Status429TooManyRequests,
                PermitLimit = rule.PermitLimit,
                RequestCount = result.CurrentCount,
                RetryAfterSeconds = result.RetryAfterSeconds,
                QueryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                CorrelationId = context.TraceIdentifier,
                TimestampUtc = DateTimeOffset.UtcNow
            });

            _logger.LogWarning(
                "Client {ClientKey} ({Tier}) exceeded rate limit on {Method} {Path}. Permit limit: {Limit}, Retry after: {RetryAfter}s",
                client.ClientKey, client.Tier, context.Request.Method, path, rule.PermitLimit, result.RetryAfterSeconds);

            // Return RFC 7807 compliant ProblemDetails
            var problemDetails = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807#section-6.5.9",
                Title = "Too Many Requests",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = $"Rate limit quota of {rule.PermitLimit} requests per {rule.WindowSeconds} seconds exceeded. Please wait {result.RetryAfterSeconds} seconds before retrying.",
                Instance = path
            };

            problemDetails.Extensions["clientKey"] = client.ClientKey;
            problemDetails.Extensions["tier"] = client.Tier;
            problemDetails.Extensions["retryAfterSeconds"] = result.RetryAfterSeconds;
            problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

            await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOptions));
            return;
        }

        // Request permitted: continue pipeline
        await _next(context);
    }

    private static bool ShouldBypass(string path)
    {
        return path == "/" ||
               path.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase);
    }

    private RateLimitRule ResolveMatchingRule(string path, string httpMethod, string tier)
    {
        // Check custom configured endpoint rules first
        if (_options.EndpointRules.Count > 0)
        {
            foreach (var r in _options.EndpointRules)
            {
                var methodMatches = string.Equals(r.HttpMethod, "*", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(r.HttpMethod, httpMethod, StringComparison.OrdinalIgnoreCase);

                if (methodMatches && IsPathMatch(r.EndpointPattern, path))
                {
                    return r;
                }
            }
        }

        // Next check tier configuration
        if (_options.TierLimits.TryGetValue(tier, out var tierConfig))
        {
            return new RateLimitRule
            {
                RuleName = $"{tier}-Policy",
                Tier = tier,
                PermitLimit = tierConfig.PermitLimit,
                WindowSeconds = tierConfig.WindowSeconds,
                EndpointPattern = "*",
                HttpMethod = "*"
            };
        }

        // Default fallback rule
        return new RateLimitRule
        {
            RuleName = "Default-Policy",
            Tier = tier,
            PermitLimit = _options.DefaultPermitLimit,
            WindowSeconds = _options.DefaultWindowSeconds,
            EndpointPattern = "*",
            HttpMethod = "*"
        };
    }

    private static bool IsPathMatch(string pattern, string path)
    {
        if (pattern == "*" || string.Equals(pattern, path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.EndsWith("/*"))
        {
            var prefix = pattern[..^2];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
