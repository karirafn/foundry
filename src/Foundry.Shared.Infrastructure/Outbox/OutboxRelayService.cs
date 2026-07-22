using System.Text.Json;

using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelayService> logger) : PeriodicBackgroundService(logger)
{
    private readonly OutboxOptions _options = options.Value;
    private DateTimeOffset _lastPruneAt = DateTimeOffset.MinValue;

    protected override TimeSpan TickInterval => _options.TickInterval;

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IIntegrationEventProcessor processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();

        List<OutboxMessage> batch = await dbContext.FindUnpublishedBatchAsync(
            _options.BatchSize,
            _options.MaxAttempts,
            cancellationToken);

        foreach (OutboxMessage message in batch)
        {
            await ProcessMessageAsync(dbContext, processor, message, cancellationToken);
        }

        await RunRetentionSweepIfDueAsync(dbContext, cancellationToken);
    }

    private async Task RunRetentionSweepIfDueAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (now - _lastPruneAt < _options.RetentionSweepInterval)
        {
            return;
        }

        DateTimeOffset olderThan = now - _options.RetentionWindow;
        int deleted = await dbContext.PrunePublishedAsync(olderThan, cancellationToken);
        _lastPruneAt = now;

        if (deleted > 0)
        {
            logger.LogInformation(
                "Outbox retention sweep deleted {Count} published message(s) older than {OlderThan:O}.",
                deleted,
                olderThan);
        }
    }

    private async Task ProcessMessageAsync(
        DbContext dbContext,
        IIntegrationEventProcessor processor,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        Type? eventType = Type.GetType(message.Type);

        if (eventType is null)
        {
            message.RecordFailure($"Cannot resolve type '{message.Type}'. The assembly may have been renamed or the type deleted.");
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Outbox message {MessageId} has an unresolvable type '{Type}'; dead-lettering.",
                message.Id,
                message.Type);
            return;
        }

        IIntegrationEvent? @event;

        try
        {
            @event = (IIntegrationEvent?)JsonSerializer.Deserialize(message.Payload, eventType, OutboxSerializerOptions.Default);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message.RecordFailure($"Deserialization failed: {ex.Message}");
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                ex,
                "Outbox message {MessageId} failed to deserialize as '{Type}'; dead-lettering.",
                message.Id,
                message.Type);
            return;
        }

        if (@event is null)
        {
            message.RecordFailure("Deserialization returned null.");
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Outbox message {MessageId} deserialized to null; dead-lettering.",
                message.Id);
            return;
        }

        try
        {
            await processor.ProcessAsync(message.Id, @event, cancellationToken);
            message.MarkPublished(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message.RecordFailure(ex.Message);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                ex,
                "Outbox message {MessageId} processor threw; attempt {Attempts} of {MaxAttempts}.",
                message.Id,
                message.Attempts,
                _options.MaxAttempts);
        }
    }
}
