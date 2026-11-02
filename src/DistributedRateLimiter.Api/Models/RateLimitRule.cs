namespace DistributedRateLimiter.Api.Models;

/// <summary>
/// Configuration rule defining rate limiting parameters for a specific route or client tier.
/// </summary>
public class RateLimitRule
{
    /// <summary>
    /// Unique name of the rate limiting rule.
    /// </summary>
    public string RuleName { get; set; } = "Default";

    /// <summary>
    /// Path pattern to match against (e.g. "/api/*", "/api/products/*", or "*").
    /// </summary>
    public string EndpointPattern { get; set; } = "*";

    /// <summary>
    /// HTTP method to apply this rule to, or "*" for all methods.
    /// </summary>
    public string HttpMethod { get; set; } = "*";

    /// <summary>
    /// Maximum number of requests allowed within the defined sliding window.
    /// </summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>
    /// Sliding window time span in seconds.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Client tier associated with this rule (e.g. "Anonymous", "Standard", "Premium").
    /// </summary>
    public string Tier { get; set; } = "Default";
}
