namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// A revision queued or held by open blockers, as the review pass reads it: only whether its pull request closed
/// without merging, which fails the revision before it starts.
/// </summary>
public sealed record QueuedRevisionIssueInfo(int IssueNumber, string PullRequestUrl);
