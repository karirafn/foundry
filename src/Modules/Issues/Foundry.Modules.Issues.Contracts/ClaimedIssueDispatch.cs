using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;

using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public sealed record ClaimedIssueDispatch(
    IssueId IssueId,
    WorkerRunId WorkerRunId,
    int IssueNumber,
    string Title,
    string Body,
    string RepositorySlug,
    Uri CloneUrl,
    string? AccountToken,
    BranchName BranchName,
    MonitoredRepositoryId MonitoredRepositoryId,
    WorkerProvider Provider,
    DispatchContext Context,
    string IssueApiUrl);
