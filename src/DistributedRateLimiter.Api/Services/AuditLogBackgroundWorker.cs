using DistributedRateLimiter.Api.Data;
using DistributedRateLimiter.Api.Models.Entities;

namespace DistributedRateLimiter.Api.Services;

/// <summary>
/// Background hosted service continuously draining rate limit violations from the channel
/// and persisting them in batches to PostgreSQL via EF Core.
/// </summary>
public class AuditLogBackgroundWorker : BackgroundService
{
    private readonly AuditLogService _auditLogService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogBackgroundWorker> _logger;
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    public AuditLogBackgroundWorker(
        AuditLogService auditLogService,
        IServiceScopeFactory scopeFactory,
        ILogger<AuditLogBackgroundWorker> logger)
    {
        _auditLogService = auditLogService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rate Limit Audit Log background worker started.");

        var batch = new List<RateLimitViolationLog>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for an item to be ready
                if (await _auditLogService.Reader.WaitToReadAsync(stoppingToken))
                {
                    while (batch.Count < BatchSize && _auditLogService.Reader.TryRead(out var log))
                    {
                        batch.Add(log);
                    }

                    if (batch.Count > 0)
                    {
                        await PersistBatchAsync(batch, stoppingToken);
                        batch.Clear();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing audit log batch. Retrying after pause.");
                await Task.Delay(FlushInterval, stoppingToken);
            }
        }

        // Drain any remaining records upon graceful shutdown
        while (_auditLogService.Reader.TryRead(out var log))
        {
            batch.Add(log);
        }

        if (batch.Count > 0)
        {
            try
            {
                await PersistBatchAsync(batch, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist audit log batch during shutdown.");
            }
        }

        _logger.LogInformation("Rate Limit Audit Log background worker stopped.");
    }

    private async Task PersistBatchAsync(List<RateLimitViolationLog> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RateLimiterDbContext>();

            await dbContext.RateLimitViolations.AddRangeAsync(batch, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Persisted batch of {Count} rate limit violations to PostgreSQL.", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist batch of {Count} rate limit violations to PostgreSQL.", batch.Count);
        }
    }
}
