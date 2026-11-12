using System.Collections.Concurrent;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>Records every <see cref="ProviderBranchPushed"/> the webhook ingress publishes.</summary>
internal sealed class ProviderBranchPushLog
{
    private readonly ConcurrentQueue<(MonitoredRepositoryId RepositoryId, string Branch)> _pushes = new();

    public void Add(MonitoredRepositoryId repositoryId, string branch) => _pushes.Enqueue((repositoryId, branch));

    public bool Contains(MonitoredRepositoryId repositoryId, string branch) => _pushes.Contains((repositoryId, branch));
}

[IntegrationEventHandlerIdentity("IntegrationTests.ProviderBranchPushRecorder")]
internal sealed class ProviderBranchPushRecorder(ProviderBranchPushLog log) : IIntegrationEventHandler<ProviderBranchPushed>
{
    public Task HandleAsync(ProviderBranchPushed @event, CancellationToken cancellationToken)
    {
        log.Add(@event.RepositoryId, @event.Branch);
        return Task.CompletedTask;
    }
}
