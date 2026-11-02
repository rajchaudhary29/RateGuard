namespace DistributedRateLimiter.Api.Options;

/// <summary>
/// Configuration settings for connecting to the distributed Redis instance.
/// </summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Host connection string (e.g., "localhost:6379" or "redis:6379").
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Key prefix applied to all rate limit entries in Redis.
    /// </summary>
    public string KeyPrefix { get; set; } = "ratelimit:";

    /// <summary>
    /// Connection timeout in milliseconds.
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Synchronous command timeout in milliseconds.
    /// </summary>
    public int SyncTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// If false, connection multiplexer will retry in the background rather than throw immediately.
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;
}
