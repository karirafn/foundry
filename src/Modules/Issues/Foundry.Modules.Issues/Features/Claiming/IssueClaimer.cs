using System.Globalization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Executes the claim step for a selected <see cref="DispatchCandidate"/>: transitions the
/// aggregate to its in-progress state, dispatches the <see cref="IssueClaimed"/> integration
/// event, and persists the transition atomically.
/// </summary>
internal sealed class IssueClaimer(
    DbContext db,
    IRepositoryRemovalQueries repositoryRemoval,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider)
{
    /// <summary>Claims the candidate, or returns false without writing when its repository has been removed.</summary>
    public async Task<bool> ClaimAsync(
        DispatchCandidate candidate,
        WorkerRunId workerRunId,
        CancellationToken cancellationToken)
    {
        QueuedIssue issue = candidate.Issue;

        // The removal check runs inside the claim's write transaction, and a removal checks for in-flight issues inside
        // its own: SQLite serializes the two, so either this claim sees the removal or the removal sees this claim.
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (await repositoryRemoval.IsRemovedAsync(issue.MonitoredRepositoryId, cancellationToken))
        {
            return false;
        }

        BranchName? branchName = issue.DispatchBranchName;
        Issue claimed = issue.Claim(workerRunId, timeProvider.GetUtcNow());

        string issueApiUrl = candidate.DispatchInfo.IssueApiUrlBase
            + "/" + claimed.IssueNumber.ToString(CultureInfo.InvariantCulture);

        ClaimedIssueDispatch dispatch = new(
            claimed.Id,
            workerRunId,
            claimed.IssueNumber,
            claimed.Title,
            candidate.DispatchInfo.RepositorySlug,
            candidate.DispatchInfo.CloneUrl,
            branchName,
            issue.MonitoredRepositoryId,
            candidate.DispatchInfo.Provider,
            issue.Context,
            issueApiUrl,
            issue.Clarifications);

        await integrationEventDispatcher.DispatchAsync([new IssueClaimed(dispatch)], cancellationToken);

        await db.TransitionAsync(issue, claimed, domainEventDispatcher, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
