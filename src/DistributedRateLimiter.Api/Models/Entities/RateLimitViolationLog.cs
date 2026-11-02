using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DistributedRateLimiter.Api.Models.Entities;

/// <summary>
/// Entity representing an audited rate limit violation recorded in PostgreSQL.
/// </summary>
[Table("rate_limit_violation_logs")]
public class RateLimitViolationLog
{
    /// <summary>
    /// Unique identifier for the audit record.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// Identifier representing the client (API Key, Token, or Client IP).
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string ClientKey { get; set; } = string.Empty;

    /// <summary>
    /// Client remote IP address.
    /// </summary>
    [MaxLength(64)]
    public string ClientIp { get; set; } = string.Empty;

    /// <summary>
    /// Client tier evaluated for the request (e.g. Anonymous, Standard, Premium).
    /// </summary>
    [MaxLength(64)]
    public string Tier { get; set; } = string.Empty;

    /// <summary>
    /// The requested path.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string RequestPath { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method used (GET, POST, etc.).
    /// </summary>
    [Required]
    [MaxLength(16)]
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code returned (429).
    /// </summary>
    public int StatusCode { get; set; } = StatusCodes.Status429TooManyRequests;

    /// <summary>
    /// Configured permit limit for the evaluated time window.
    /// </summary>
    public int PermitLimit { get; set; }

    /// <summary>
    /// Request count recorded during the sliding window when violation occurred.
    /// </summary>
    public long RequestCount { get; set; }

    /// <summary>
    /// Number of seconds the client must wait before retrying.
    /// </summary>
    public int RetryAfterSeconds { get; set; }

    /// <summary>
    /// Request Query String if provided.
    /// </summary>
    [MaxLength(2048)]
    public string? QueryString { get; set; }

    /// <summary>
    /// User Agent string if provided in request headers.
    /// </summary>
    [MaxLength(512)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Distributed tracing correlation identifier or traceparent.
    /// </summary>
    [MaxLength(128)]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Timestamp (UTC) when the violation occurred.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
