using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;

using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

/// <param name="BranchName">The branch the run works on, or <see langword="null"/> for a run that gets none (triage).</param>
/// <param name="Clarifications">
/// The issue's clarification log (ADR 0096), rendered into the run's prompt. Optional only so claims enqueued before
/// the log existed still deserialize; <see langword="null"/> reads as an empty log.
/// </param>
public sealed record ClaimedIssueDispatch(
    IssueId IssueId,
    WorkerRunId WorkerRunId,
    int IssueNumber,
    string Title,
    string RepositorySlug,
    Uri CloneUrl,
    BranchName? BranchName,
    MonitoredRepositoryId MonitoredRepositoryId,
    WorkerProvider Provider,
    DispatchContext Context,
    string IssueApiUrl,
    IReadOnlyList<Clarification>? Clarifications = null);
