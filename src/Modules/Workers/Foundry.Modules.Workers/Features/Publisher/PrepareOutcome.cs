using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Publisher;

internal abstract record PrepareOutcome
{
    private PrepareOutcome()
    {
    }

    /// <summary>The base bundle is on the Hand-off volume; <see cref="Base"/> is what it holds.</summary>
    public sealed record Prepared(RunBase Base) : PrepareOutcome;

    /// <summary>Prepare did not produce a base bundle, so no worker may start.</summary>
    public sealed record Failed(FailureReason Reason) : PrepareOutcome;
}
