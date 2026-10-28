using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Testing;

public sealed class ChecksOutcomeBuilder
{
    private IReadOnlyList<FailingCheck> _failingChecks = [new FailingCheckBuilder().Build()];

    public ChecksOutcomeBuilder WithFailingChecks(IEnumerable<FailingCheck> failingChecks)
    {
        _failingChecks = [.. failingChecks];
        return this;
    }

    public ChecksOutcomeBuilder WithFailingCheck(FailingCheck failingCheck)
    {
        _failingChecks = [.. _failingChecks, failingCheck];
        return this;
    }

    public static ChecksOutcome.Passed Passed() => new();

    public static ChecksOutcome.NoneDetected NoneDetected() => new();

    public static ChecksOutcome.Unfinished Unfinished() => new();

    public ChecksOutcome.StillFailing StillFailing() => new(FailingChecks: _failingChecks);

    public static ChecksOutcome.Unreadable Unreadable() => new();
}
