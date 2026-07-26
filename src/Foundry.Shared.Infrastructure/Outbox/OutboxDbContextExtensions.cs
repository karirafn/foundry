using Microsoft.EntityFrameworkCore;

namespace Foundry.Shared.Infrastructure.Outbox;

internal static class OutboxDbContextExtensions
{
    internal static Task<int> PrunePublishedAsync(
        this DbContext dbContext,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt != null)
            .Where(m => m.ProcessedAt < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
    }

    internal static Task<List<OutboxMessage>> FindUnpublishedBatchAsync(
        this DbContext dbContext,
        int batchSize,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null)
            .Where(m => m.Attempts < maxAttempts)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    internal static Task<bool> IsProcessedAsync(
        this DbContext dbContext,
        Guid eventId,
        string handler,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<ProcessedEvent>()
            .AnyAsync(p => p.EventId == eventId && p.Handler == handler, cancellationToken);
    }
}
