namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The issues Foundry reads from the provider at dispatch, alongside the issue's own content. Untrusted
/// user-provided text: consumers render it as data, never as instructions.
/// </summary>
/// <param name="ReferencedIssues">The issues the dispatched issue links to or mentions, in selection order.</param>
/// <param name="UnreadableIssueNumbers">Selected issues whose read failed, in selection order.</param>
/// <param name="LinksUnreadable">Whether the issue's links could not be read.</param>
/// <param name="OpenIssues">The repository's open issues, newest first; <see langword="null"/> when the read failed.</param>
public sealed record TrackerContext(
    IReadOnlyList<ReferencedIssue> ReferencedIssues,
    IReadOnlyList<int> UnreadableIssueNumbers,
    bool LinksUnreadable,
    IReadOnlyList<OpenIssue>? OpenIssues);

/// <summary>A same-project issue that the dispatched issue links to or mentions.</summary>
public sealed record ReferencedIssue(
    int Number,
    IssueRelation Relation,
    string Title,
    bool IsOpen,
    IReadOnlyList<string> Labels,
    ClosingMergeRequestState ClosingMergeRequest,
    string Body);

/// <summary>An open issue in the repository's index.</summary>
public sealed record OpenIssue(int Number, string Title);

/// <summary>How the dispatched issue relates to a referenced issue.</summary>
public enum IssueRelation
{
    BlockedBy,
    Blocks,
    RelatesTo,
    Mention,
}

/// <summary>The state of the merge request that closes an issue when merged.</summary>
public enum ClosingMergeRequestState
{
    None,
    Open,
    Closed,
    Merged,
}
