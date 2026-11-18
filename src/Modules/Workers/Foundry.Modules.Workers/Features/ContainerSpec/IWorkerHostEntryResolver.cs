namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>Chooses the worker's <c>/etc/hosts</c> entry that maps <c>host.docker.internal</c> to Foundry's host.</summary>
internal interface IWorkerHostEntryResolver
{
    Task<string> ResolveAsync(CancellationToken cancellationToken);
}
