using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Commits <see cref="FreshQueuedIssue.AdoptBranch"/>: the queued issue becomes a <see cref="ContinuationQueuedIssue"/>
/// on its existing branch, so a later dispatch pass claims it as a Continuation.
/// </summary>
internal sealed class FreshBranchAdopter(
    DbContext db,
    IDomainEventDispatcher dispatcher,
    TimeProvider timeProvider)
{
    public async Task AdoptAsync(FreshQueuedIssue queued, CancellationToken cancellationToken)
    {
        ContinuationQueuedIssue continuation = queued.AdoptBranch(timeProvider.GetUtcNow());
        await db.TransitionAsync(queued, continuation, dispatcher, cancellationToken);
    }
}
