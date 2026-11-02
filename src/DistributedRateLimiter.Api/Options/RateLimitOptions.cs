using DistributedRateLimiter.Api.Models;

namespace DistributedRateLimiter.Api.Options;

/// <summary>
/// Configuration options for the distributed rate limiter.
/// </summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Default request limit per window for anonymous or unclassified requests.
    /// </summary>
    public int DefaultPermitLimit { get; set; } = 10;

    /// <summary>
    /// Default sliding window duration in seconds.
    /// </summary>
    public int DefaultWindowSeconds { get; set; } = 60;

    /// <summary>
    /// HTTP header name inspected for API keys or client tokens.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Whether to inspect reverse-proxy headers like X-Forwarded-For to determine client IP.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; } = true;

    /// <summary>
    /// Key-value mappings of API Keys to Client Tiers.
    /// Example: "dev-key-123": "Standard", "prod-key-999": "Premium".
    /// </summary>
    public Dictionary<string, string> ApiKeyTiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tier-specific limit configurations.
    /// </summary>
    public Dictionary<string, TierLimitConfig> TierLimits { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Anonymous"] = new() { PermitLimit = 5, WindowSeconds = 10 },
        ["Standard"] = new() { PermitLimit = 20, WindowSeconds = 10 },
        ["Premium"] = new() { PermitLimit = 100, WindowSeconds = 10 }
    };

    /// <summary>
    /// Custom path/method specific rate limit rules.
    /// </summary>
    public List<RateLimitRule> EndpointRules { get; set; } = new();
}

/// <summary>
/// Rate limit configuration for a specific client tier.
/// </summary>
public class TierLimitConfig
{
    /// <summary>
    /// Max requests allowed in the window.
    /// </summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>
    /// Sliding window time span in seconds.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;
}
