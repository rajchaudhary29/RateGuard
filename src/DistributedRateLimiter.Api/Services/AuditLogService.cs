using System.Threading.Channels;
using DistributedRateLimiter.Api.Models.Entities;

namespace DistributedRateLimiter.Api.Services;

/// <summary>
/// High-throughput channel-backed implementation of <see cref="IAuditLogService"/>
/// preventing database connection pool exhaustion during traffic surges.
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly Channel<RateLimitViolationLog> _channel;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ILogger<AuditLogService> logger)
    {
        _logger = logger;
        // Bounded channel to enforce backpressure under extreme DDoS scenarios
        var options = new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<RateLimitViolationLog>(options);
    }

    /// <summary>
    /// Exposes reader to the background hosted service.
    /// </summary>
    public ChannelReader<RateLimitViolationLog> Reader => _channel.Reader;

    /// <inheritdoc />
    public void LogViolation(RateLimitViolationLog violation)
    {
        if (!_channel.Writer.TryWrite(violation))
        {
            _logger.LogWarning("Audit log channel buffer full. Oldest log dropped for client {ClientKey}.", violation.ClientKey);
        }
    }
}
