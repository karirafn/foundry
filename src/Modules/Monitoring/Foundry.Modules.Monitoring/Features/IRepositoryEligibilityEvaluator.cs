using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Modules.Monitoring.Features;

internal interface IRepositoryEligibilityEvaluator
{
    Task EvaluateAndStoreAsync(
        MonitoredRepository repo,
        CancellationToken cancellationToken);
}
