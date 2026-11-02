namespace DistributedRateLimiter.Api.Models;

/// <summary>
/// Result returned by the distributed rate limiter service evaluating a request against Redis.
/// </summary>
public class RateLimitResult
{
    /// <summary>
    /// Indicates whether the request is allowed through the middleware pipeline.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Total count of requests in the sliding window including this request if permitted.
    /// </summary>
    public long CurrentCount { get; init; }

    /// <summary>
    /// The maximum number of requests allowed for this client in the current window.
    /// </summary>
    public int Limit { get; init; }

    /// <summary>
    /// Number of remaining requests permitted before reaching the limit.
    /// </summary>
    public long Remaining { get; init; }

    /// <summary>
    /// Estimated UTC time when the rate limit window will reset or expire oldest request.
    /// </summary>
    public DateTimeOffset ResetTimeUtc { get; init; }

    /// <summary>
    /// Number of seconds the client should wait before making subsequent requests.
    /// </summary>
    public int RetryAfterSeconds { get; init; }

    /// <summary>
    /// The client identifier key used for rate limit accounting.
    /// </summary>
    public string ClientKey { get; init; } = string.Empty;

    /// <summary>
    /// The rule that was evaluated.
    /// </summary>
    public string RuleName { get; init; } = string.Empty;

    /// <summary>
    /// Factory method for permitted requests.
    /// </summary>
    public static RateLimitResult Permitted(
        string clientKey,
        string ruleName,
        int limit,
        long currentCount,
        long remaining,
        DateTimeOffset resetTimeUtc) => new()
    {
        IsAllowed = true,
        ClientKey = clientKey,
        RuleName = ruleName,
        Limit = limit,
        CurrentCount = currentCount,
        Remaining = remaining,
        ResetTimeUtc = resetTimeUtc,
        RetryAfterSeconds = 0
    };

    /// <summary>
    /// Factory method for throttled/blocked requests.
    /// </summary>
    public static RateLimitResult Throttled(
        string clientKey,
        string ruleName,
        int limit,
        long currentCount,
        int retryAfterSeconds,
        DateTimeOffset resetTimeUtc) => new()
    {
        IsAllowed = false,
        ClientKey = clientKey,
        RuleName = ruleName,
        Limit = limit,
        CurrentCount = currentCount,
        Remaining = 0,
        ResetTimeUtc = resetTimeUtc,
        RetryAfterSeconds = retryAfterSeconds > 0 ? retryAfterSeconds : 1
    };
}
