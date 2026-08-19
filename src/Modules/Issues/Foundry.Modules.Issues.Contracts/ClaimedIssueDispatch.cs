using Foundry.Modules.Monitoring.Contracts;

using Foundry.Shared;

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
    BranchName BranchName,
    MonitoredRepositoryId MonitoredRepositoryId,
    WorkerProvider Provider,
    RevisionContext? Revision = null,
    ContinuationContext? Continuation = null);
