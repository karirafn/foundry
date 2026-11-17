using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Re-evaluates and stores the eligibility of every active repository that declares a source whose readability
/// changed, so none keeps a stale <c>package-source-unreachable</c> verdict until its own next poll. Every declaring
/// repository is re-evaluated, whichever credential flipped: the evaluator reads the verdict for each repository's
/// own credential. It reads only stored verdicts, so it never re-enters <see cref="PackageSourceProbing"/>.
/// </summary>
internal sealed class PackageSourceReadabilityChangedHandler(
    DbContext dbContext,
    IRepositoryEligibilityEvaluator eligibilityEvaluator) : IDomainEventHandler<PackageSourceReadabilityChanged>
{
    public async Task HandleAsync(PackageSourceReadabilityChanged @event, CancellationToken cancellationToken)
    {
        List<MonitoredRepository> active = await dbContext.Set<MonitoredRepository>()
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);

        // Declared sources are stored as JSON, so the match runs in memory.
        IEnumerable<MonitoredRepository> declaring = active.Where(r =>
            r.DeclaredPackageSources.Any(s => s.ServiceIndexUrl.AbsoluteUri == @event.ServiceIndexUrl.AbsoluteUri));

        foreach (MonitoredRepository repository in declaring)
        {
            await eligibilityEvaluator.EvaluateBranchRulesAndStoreAsync(repository, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
