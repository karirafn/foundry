using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// What the awaiting-input poll step needs of one waiting issue (ADR 0096). The questions go on
/// <paramref name="PullRequestUrl"/>'s thread when it is set, otherwise on the issue's own thread.
/// </summary>
public sealed record AwaitingInputIssueInfo(
    IssueId IssueId,
    int IssueNumber,
    string IssueUrl,
    string? PullRequestUrl,
    IReadOnlyList<InputQuestion> Questions,
    AwaitingInputPostState PostState);

/// <summary>How an awaiting-input issue's question post stands, as the poll step acts on it.</summary>
public abstract record AwaitingInputPostState
{
    private AwaitingInputPostState()
    {
    }

    /// <summary>
    /// Not posted yet. Due at <paramref name="DueAt"/>; <see langword="null"/> after a permanent failure, which is not
    /// retried automatically.
    /// </summary>
    public sealed record Unposted(DateTimeOffset? DueAt) : AwaitingInputPostState;

    /// <summary>Posted at <paramref name="PostedAt"/>, the cutoff replies are read after.</summary>
    public sealed record Posted(DateTimeOffset PostedAt) : AwaitingInputPostState;
}
