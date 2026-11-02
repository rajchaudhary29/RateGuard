using DistributedRateLimiter.Api.Models;

namespace DistributedRateLimiter.Api.Services;

/// <summary>
/// Service contract for distributed rate limiting operations against Redis.
/// </summary>
public interface IRateLimiterService
{
    /// <summary>
    /// Evaluates if an incoming request for a given client and rule should be allowed.
    /// </summary>
    /// <param name="clientKey">Unique identifier of the client (IP or API Key).</param>
    /// <param name="rule">The rate limit rule defining the quota and sliding window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="RateLimitResult"/> indicating whether the request is allowed, remaining quota, and reset metadata.</returns>
    Task<RateLimitResult> CheckRateLimitAsync(string clientKey, RateLimitRule rule, CancellationToken cancellationToken = default);
}
