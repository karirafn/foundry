using Foundry.Modules.Monitoring.Contracts;

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
    string BranchName,
    MonitoredRepositoryId MonitoredRepositoryId,
    RevisionContext? Revision = null,
    ContinuationContext? Continuation = null);
