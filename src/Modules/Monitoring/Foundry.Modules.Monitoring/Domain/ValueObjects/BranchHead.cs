namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// A branch's head as one provider read reports it (ADR 0096): the commit it points at, or that the branch does not
/// exist. A read that fails is neither, and is a failed result instead.
/// </summary>
public abstract record BranchHead
{
    private BranchHead()
    {
    }

    /// <summary>The branch exists and points at <paramref name="Sha"/>.</summary>
    public sealed record Present(string Sha) : BranchHead
    {
        public string Sha { get; } = !string.IsNullOrWhiteSpace(Sha)
            ? Sha
            : throw new ArgumentException("A branch head's SHA is required.", nameof(Sha));
    }

    /// <summary>The branch does not exist.</summary>
    public sealed record Absent : BranchHead;
}
