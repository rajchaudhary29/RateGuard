using DistributedRateLimiter.Api.Middleware;

namespace DistributedRateLimiter.Api.Extensions;

/// <summary>
/// Extension methods for registering rate limiter middleware components in the ASP.NET Core pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds global exception handling middleware returning RFC 7807 ProblemDetails.
    /// </summary>
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
    }

    /// <summary>
    /// Adds the distributed sliding-window rate limiting middleware.
    /// </summary>
    public static IApplicationBuilder UseDistributedRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RateLimitingMiddleware>();
    }
}
