using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Testing;

/// <summary>
/// Builds <see cref="RateBudgetReading"/> instances for use in tests.
/// Defaults to a fresh reading with 1000 remaining — healthy against the default floor.
/// </summary>
public sealed class RateBudgetReadingBuilder
{
    private int _remaining = 1000;
    private int? _limit = 5000;
    private DateTimeOffset? _resetAt;
    private DateTimeOffset _observedAt = DateTimeOffset.UtcNow;

    public RateBudgetReadingBuilder WithRemaining(int remaining)
    {
        _remaining = remaining;
        return this;
    }

    public RateBudgetReadingBuilder WithLimit(int? limit)
    {
        _limit = limit;
        return this;
    }

    public RateBudgetReadingBuilder WithResetAt(DateTimeOffset? resetAt)
    {
        _resetAt = resetAt;
        return this;
    }

    public RateBudgetReadingBuilder WithObservedAt(DateTimeOffset observedAt)
    {
        _observedAt = observedAt;
        return this;
    }

    internal RateBudgetReading Build() =>
        new(_remaining, _limit, _resetAt, _observedAt);
}
