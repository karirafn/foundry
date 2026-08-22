using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

internal interface IRepositoryEligibilityEvaluator
{
    /// <summary>
    /// Runs the write probe (expensive, event-triggered), persists the fresh verdict,
    /// then composes and stores eligibility from verdict + branch rules.
    /// </summary>
    Task EvaluateFullyAndStoreAsync(
        MonitoredRepository repo,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs only the branch-rules GET (cheap, per-cycle), reads the stored verdict,
    /// composes and stores eligibility — issues no write probe.
    /// </summary>
    Task EvaluateBranchRulesAndStoreAsync(
        MonitoredRepository repo,
        CancellationToken cancellationToken);
}
