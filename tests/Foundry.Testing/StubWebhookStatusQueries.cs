using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>Stub for <see cref="IWebhookStatusQueries"/>: every repository reports <paramref name="isActive"/>.</summary>
public sealed class StubWebhookStatusQueries(bool isActive = false) : IWebhookStatusQueries
{
    public Task<bool> IsWebhookActiveAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken)
        => Task.FromResult(isActive);
}
