namespace Foundry.Modules.Issues.Contracts;

public sealed record ClaimedIssueDispatch(
    IssueId IssueId,
    Guid WorkerRunId,
    int IssueNumber,
    string Title,
    string Body,
    string RepositorySlug,
    Uri CloneUrl,
    string? AccountToken,
    RevisionContext? Revision = null,
    ContinuationContext? Continuation = null);
