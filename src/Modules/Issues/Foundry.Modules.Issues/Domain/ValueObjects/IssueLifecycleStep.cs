namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// One position on an issue's lifecycle path, in path order: the queue waits come before Triage, Implement and Review.
/// </summary>
public enum IssueLifecycleStep
{
    Detected,
    TriageQueued,
    Triage,
    Queued,
    Implement,
    Publish,
    Rebase,
    Checks,
    ReviewQueued,
    Review,
    YourReview,
    Merge,
    Done,
}
