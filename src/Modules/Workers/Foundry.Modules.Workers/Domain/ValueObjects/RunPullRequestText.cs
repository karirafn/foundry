namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The pull request text an implement run's verdict carried (<c>pullRequest</c>), as the run wrote it. It is validated
/// and scrubbed where it is used, so a later fix to the rules applies to runs already waiting.
/// </summary>
public sealed record RunPullRequestText(string? Summary, RunPullRequestDescription? Description);

/// <summary>
/// The structured description fields of an implement verdict's <c>pullRequest</c>, as the run wrote them;
/// <see cref="PullRequestDescription"/> holds them once validated and scrubbed.
/// </summary>
public sealed record RunPullRequestDescription(
    string? Cause,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Verification,
    IReadOnlyList<string> Attention,
    string? Notes)
{
    public bool Equals(RunPullRequestDescription? other) =>
        other is not null
        && Cause == other.Cause
        && Notes == other.Notes
        && Changes.SequenceEqual(other.Changes)
        && Verification.SequenceEqual(other.Verification)
        && Attention.SequenceEqual(other.Attention);

    public override int GetHashCode() => HashCode.Combine(Cause, Notes, Changes.Count, Verification.Count, Attention.Count);
}
