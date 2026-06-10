namespace Foundry.Modules.Issues.Contracts;

public sealed record ClaimedIssueDispatch(
    IssueId IssueId,
    Guid WorkerRunId,
    int IssueNumber,
    string Title,
    string Body,
    string RepositorySlug,
    Uri CloneUrl,
    string AccountSecretKeyName,
    RevisionContext? Revision = null,
    ContinuationContext? Continuation = null)
{
    // Field exists only to enforce the invariant at construction time via its initializer.
#pragma warning disable CA1823
    private readonly bool _ = Revision is not null && Continuation is not null
        ? throw new ArgumentException("Revision and Continuation are mutually exclusive.")
        : true;
#pragma warning restore CA1823
}
