using Microsoft.EntityFrameworkCore;

namespace Foundry.Shared.Infrastructure.Outbox;

internal static class OutboxDbContextExtensions
{
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
}
