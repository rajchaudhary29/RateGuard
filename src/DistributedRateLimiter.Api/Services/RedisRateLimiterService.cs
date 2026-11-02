using DistributedRateLimiter.Api.Models;
using DistributedRateLimiter.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DistributedRateLimiter.Api.Services;

/// <summary>
/// Production-ready distributed sliding-window rate limiter using Redis Sorted Sets (ZSET)
/// and atomic Lua script execution.
/// </summary>
public class RedisRateLimiterService : IRateLimiterService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisRateLimiterService> _logger;

    // Lua script executing the sliding-window log counter atomically inside Redis:
    // ARGV[1] = now_ms
    // ARGV[2] = window_ms
    // ARGV[3] = permit_limit
    // ARGV[4] = unique_member_key
    // ARGV[5] = ttl_seconds
    private const string SlidingWindowLuaScript = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local member = ARGV[4]
        local ttl = tonumber(ARGV[5])

        local clear_before = now - window

        -- 1. Evict timestamps falling outside the sliding window
        redis.call('ZREMRANGEBYSCORE', key, 0, clear_before)

        -- 2. Count active elements in the window
        local current_count = redis.call('ZCARD', key)

        if current_count < limit then
            -- Allowed: Append timestamp member and set TTL
            redis.call('ZADD', key, now, member)
            redis.call('EXPIRE', key, ttl)
            local remaining = limit - (current_count + 1)
            return { 1, current_count + 1, remaining, 0 }
        else
            -- Throttled: Fetch earliest active request timestamp to compute accurate Retry-After
            local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
            local retry_after_ms = 1000
            if oldest and #oldest >= 2 then
                local oldest_ts = tonumber(oldest[2])
                local wait_time = (oldest_ts + window) - now
                if wait_time > 0 then
                    retry_after_ms = wait_time
                end
            end
            redis.call('EXPIRE', key, ttl)
            return { 0, current_count, 0, retry_after_ms }
        end
    ";

    private readonly LuaScript _preparedScript;

    public RedisRateLimiterService(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        ILogger<RedisRateLimiterService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preparedScript = LuaScript.Prepare(SlidingWindowLuaScript);
    }

    /// <inheritdoc />
    public async Task<RateLimitResult> CheckRateLimitAsync(
        string clientKey,
        RateLimitRule rule,
        CancellationToken cancellationToken = default)
    {
        var redisKey = $"{_options.KeyPrefix}{clientKey}:{rule.RuleName}";
        var now = DateTimeOffset.UtcNow;
        var nowMs = now.ToUnixTimeMilliseconds();
        var windowMs = rule.WindowSeconds * 1000L;
        var ttlSeconds = Math.Max(rule.WindowSeconds * 2, 60);
        var memberKey = $"{nowMs}:{Guid.NewGuid():N}";

        try
        {
            var db = _redis.GetDatabase();
            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                _preparedScript,
                new
                {
                    key = (RedisKey)redisKey,
                    now_ms = nowMs,
                    window_ms = windowMs,
                    permit_limit = rule.PermitLimit,
                    unique_member_key = memberKey,
                    ttl_seconds = ttlSeconds
                });

            if (result == null || result.Length < 4)
            {
                _logger.LogWarning("Redis rate limiter returned invalid payload for key {RedisKey}. Falling back to permitted.", redisKey);
                return RateLimitResult.Permitted(clientKey, rule.RuleName, rule.PermitLimit, 0, rule.PermitLimit, now.AddSeconds(rule.WindowSeconds));
            }

            var isAllowed = (int)result[0] == 1;
            var currentCount = (long)result[1];
            var remaining = (long)result[2];
            var retryAfterMs = (long)result[3];

            if (isAllowed)
            {
                var resetTime = now.AddSeconds(rule.WindowSeconds);
                return RateLimitResult.Permitted(clientKey, rule.RuleName, rule.PermitLimit, currentCount, remaining, resetTime);
            }
            else
            {
                var retryAfterSeconds = (int)Math.Ceiling(retryAfterMs / 1000.0);
                if (retryAfterSeconds <= 0)
                {
                    retryAfterSeconds = 1;
                }

                var resetTime = now.AddMilliseconds(retryAfterMs);
                return RateLimitResult.Throttled(clientKey, rule.RuleName, rule.PermitLimit, currentCount, retryAfterSeconds, resetTime);
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis connection error while evaluating rate limit for key {RedisKey}. Fail-open strategy applied.", redisKey);
            // Fail open to avoid taking down critical endpoints when caching tier is temporarily unreachable
            return RateLimitResult.Permitted(clientKey, rule.RuleName, rule.PermitLimit, 1, rule.PermitLimit - 1, now.AddSeconds(rule.WindowSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error evaluating rate limit for key {RedisKey}.", redisKey);
            return RateLimitResult.Permitted(clientKey, rule.RuleName, rule.PermitLimit, 1, rule.PermitLimit - 1, now.AddSeconds(rule.WindowSeconds));
        }
    }
}
