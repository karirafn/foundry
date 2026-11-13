using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>The side branch a run's work went to instead of its own branch, and why (ADR 0100, ADR 0101).</summary>
public sealed record BranchDivergence
{
    public BranchDivergence(BranchName sideBranch, BranchDivergenceReason reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sideBranch.Value, nameof(sideBranch));
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown branch divergence reason.");
        }

        SideBranch = sideBranch;
        Reason = reason;
    }

    public BranchName SideBranch { get; }

    public BranchDivergenceReason Reason { get; }
}
