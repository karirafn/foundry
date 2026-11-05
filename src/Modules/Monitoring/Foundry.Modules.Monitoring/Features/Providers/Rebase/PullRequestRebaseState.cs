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

    /// <summary>
    /// What the provider reports about a server-side rebase of this branch (GitLab's
    /// <c>rebase_in_progress</c> / <c>merge_error</c>). Providers that only expose the head (GitHub) leave it
    /// <see cref="ProviderRebaseProgress.NotReported"/>, and landing is observed as a new head alone.
    /// </summary>
    public ProviderRebaseProgress Progress { get; init; } = ProviderRebaseProgress.NotReported.Instance;

    /// <summary>The provider has no rebase implementation.</summary>
    public sealed record Unsupported : PullRequestRebaseState;

    /// <summary>The branch already contains every base-branch commit (<c>behind_by = 0</c>).</summary>
    public sealed record UpToDate(string HeadSha) : PullRequestRebaseState;

    /// <summary>The branch is <paramref name="BehindBy"/> commits behind its base.</summary>
    public sealed record Behind(string PullRequestId, string HeadSha, int BehindBy) : PullRequestRebaseState;

    /// <summary>A permanent refusal (e.g. permission denied, not found); <see cref="Reason"/> is a skip reason.</summary>
    public sealed record Refused(string Reason) : PullRequestRebaseState;
}

/// <summary>
/// The provider's own report of a server-side rebase (ADR 0087). Only meaningful while the rebase step awaits
/// a requested rebase: before the request, a failure report may be left over from an earlier rebase.
/// </summary>
internal abstract record ProviderRebaseProgress
{
    private ProviderRebaseProgress()
    {
    }

    /// <summary>The provider does not report rebase progress (GitHub).</summary>
    public sealed record NotReported : ProviderRebaseProgress
    {
        public static readonly NotReported Instance = new();
    }

    /// <summary>A rebase is queued or running.</summary>
    public sealed record InProgress : ProviderRebaseProgress;

    /// <summary>No rebase is running and the provider reports no rebase error.</summary>
    public sealed record Completed : ProviderRebaseProgress;

    /// <summary>The last rebase failed because the branch conflicts with its base.</summary>
    public sealed record Conflicted : ProviderRebaseProgress;

    /// <summary>The last rebase failed for another reason; <see cref="Reason"/> is a skip reason.</summary>
    public sealed record Failed(string Reason) : ProviderRebaseProgress;
}
