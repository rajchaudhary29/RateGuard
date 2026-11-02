using System.Collections.Concurrent;
using DistributedRateLimiter.Api.Models;

namespace DistributedRateLimiter.Api.Services;

/// <summary>
/// In-memory sliding-window counter implementation used for local development fallback
/// when external Redis is not running on the developer workstation.
/// </summary>
public class InMemoryRateLimiterService : IRateLimiterService
{
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _requestTimestamps = new();
    private readonly ILogger<InMemoryRateLimiterService> _logger;

    public InMemoryRateLimiterService(ILogger<InMemoryRateLimiterService> logger)
    {
        _logger = logger;
        _logger.LogInformation("Using InMemoryRateLimiterService (Local Development Mode). Set 'UseInMemoryFallback: false' in appsettings to connect to distributed Redis.");
    }

    /// <inheritdoc />
    public Task<RateLimitResult> CheckRateLimitAsync(string clientKey, RateLimitRule rule, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{clientKey}:{rule.RuleName}";
        var timestamps = _requestTimestamps.GetOrAdd(cacheKey, _ => new List<DateTimeOffset>());
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddSeconds(-rule.WindowSeconds);

        lock (timestamps)
        {
            // 1. Evict entries older than sliding window
            timestamps.RemoveAll(t => t < windowStart);

            // 2. Check current count against permit limit
            if (timestamps.Count < rule.PermitLimit)
            {
                timestamps.Add(now);
                var remaining = rule.PermitLimit - timestamps.Count;
                var resetTime = now.AddSeconds(rule.WindowSeconds);
                return Task.FromResult(RateLimitResult.Permitted(clientKey, rule.RuleName, rule.PermitLimit, timestamps.Count, remaining, resetTime));
            }
            else
            {
                // Calculate accurate Retry-After based on oldest timestamp
                var oldest = timestamps.Count > 0 ? timestamps[0] : now;
                var retryAfter = (int)Math.Max(1, Math.Ceiling((oldest.AddSeconds(rule.WindowSeconds) - now).TotalSeconds));
                var resetTime = now.AddSeconds(retryAfter);
                return Task.FromResult(RateLimitResult.Throttled(clientKey, rule.RuleName, rule.PermitLimit, timestamps.Count, retryAfter, resetTime));
            }
        }
    }
}
