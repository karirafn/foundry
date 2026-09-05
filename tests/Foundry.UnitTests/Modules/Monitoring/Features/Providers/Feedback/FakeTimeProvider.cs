namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;

/// <summary>
/// A controllable <see cref="TimeProvider"/> that returns a fixed point in time,
/// making <see cref="ActionableFeedbackPolicy"/>'s quiet-period check deterministic under test.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
