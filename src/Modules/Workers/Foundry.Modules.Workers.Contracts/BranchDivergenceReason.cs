namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Why the Publisher pushed a run's work to a side branch instead of the run's branch (ADR 0100, ADR 0101).
/// </summary>
public enum BranchDivergenceReason
{
    /// <summary>
    /// Someone pushed to the run's branch during the run, and the run's work could not join it: merging a forward tip
    /// conflicted, or the tip was rebased (a rebased tip is never merged).
    /// </summary>
    BranchMoved = 0,

    /// <summary>
    /// The provider refused the lease push of a rebased tip because the branch is protected from force pushes. A retried
    /// publish would hit the same refusal.
    /// </summary>
    ForcePushProtected = 1,
}

/// <summary>
/// The stable token for each <see cref="BranchDivergenceReason"/>: the Publisher's <c>reason=</c> value, the stored
/// column value and the issue detail's <c>divergenceReason</c>.
/// </summary>
public static class BranchDivergenceReasonTokens
{
    public const string BranchMoved = "branch-moved";

    public const string ForcePushProtected = "force-push-protected";

    public static string ToToken(BranchDivergenceReason reason) => reason switch
    {
        BranchDivergenceReason.BranchMoved => BranchMoved,
        BranchDivergenceReason.ForcePushProtected => ForcePushProtected,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown branch divergence reason."),
    };

    /// <summary>The reason <paramref name="token"/> names, or <see langword="null"/> for an unknown token.</summary>
    public static BranchDivergenceReason? FromToken(string? token) => token switch
    {
        BranchMoved => BranchDivergenceReason.BranchMoved,
        ForcePushProtected => BranchDivergenceReason.ForcePushProtected,
        _ => null,
    };
}
