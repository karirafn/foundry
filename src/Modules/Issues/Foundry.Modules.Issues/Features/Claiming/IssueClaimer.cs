using System.Globalization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Executes the claim step for a selected <see cref="DispatchCandidate"/>: transitions the
/// aggregate to its in-progress state, dispatches the <see cref="IssueClaimed"/> integration
/// event, and persists the transition atomically.
/// </summary>
internal sealed class IssueClaimer(
    DbContext db,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IDomainEventDispatcher domainEventDispatcher)
{
    public async Task ClaimAsync(
        DispatchCandidate candidate,
        WorkerRunId workerRunId,
        CancellationToken cancellationToken)
    {
        QueuedIssue issue = candidate.Issue;
        BranchName branchName = issue.DispatchBranchName;
        Issue claimed = issue.Claim(workerRunId);

        string issueApiUrl = candidate.DispatchInfo.IssueApiUrlBase
            + "/" + claimed.IssueNumber.ToString(CultureInfo.InvariantCulture);

        ClaimedIssueDispatch dispatch = new(
            claimed.Id,
            workerRunId,
            claimed.IssueNumber,
            claimed.Title,
            claimed.Body,
            candidate.DispatchInfo.RepositorySlug,
            candidate.DispatchInfo.CloneUrl,
            candidate.DispatchInfo.AccountToken,
            branchName,
            issue.MonitoredRepositoryId,
            candidate.DispatchInfo.Provider,
            issue.Context,
            issueApiUrl);

        await integrationEventDispatcher.DispatchAsync([new IssueClaimed(dispatch)], cancellationToken);

        await db.TransitionAsync(issue, claimed, domainEventDispatcher, cancellationToken);
    }
}
