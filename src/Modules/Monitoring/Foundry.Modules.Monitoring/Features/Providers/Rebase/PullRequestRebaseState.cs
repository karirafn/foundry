namespace Foundry.Modules.Monitoring.Features.Providers.Rebase;

/// <summary>
/// What the provider reports about a pull request branch relative to its base (ADR 0087). Transient read
/// failures are a <c>Result</c> failure instead; every variant here is a definite answer.
/// </summary>
internal abstract record PullRequestRebaseState
{
    private PullRequestRebaseState()
    {
    }

    /// <summary>The provider has no rebase implementation.</summary>
    public sealed record Unsupported : PullRequestRebaseState;

    /// <summary>The branch already contains every base-branch commit (<c>behind_by = 0</c>).</summary>
    public sealed record UpToDate(string HeadSha) : PullRequestRebaseState;

    /// <summary>The branch is <paramref name="BehindBy"/> commits behind its base.</summary>
    public sealed record Behind(string PullRequestId, string HeadSha, int BehindBy) : PullRequestRebaseState;

    /// <summary>A permanent refusal (e.g. permission denied, not found); <see cref="Reason"/> is a skip reason.</summary>
    public sealed record Refused(string Reason) : PullRequestRebaseState;
}
