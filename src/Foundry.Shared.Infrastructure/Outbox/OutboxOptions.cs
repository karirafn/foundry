namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 100;

    public int MaxAttempts { get; set; } = 10;

    public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan RetentionSweepInterval { get; set; } = TimeSpan.FromHours(1);
}
