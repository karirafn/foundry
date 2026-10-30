namespace Foundry.Shared.Infrastructure.Outbox;

internal sealed record OutboxOptions
{
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; init; } = 100;

    public int MaxAttempts { get; init; } = 10;

    public TimeSpan RetentionWindow { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan RetentionSweepInterval { get; init; } = TimeSpan.FromHours(1);

    public int InboxPruneBatchSize { get; init; } = 5000;
}
