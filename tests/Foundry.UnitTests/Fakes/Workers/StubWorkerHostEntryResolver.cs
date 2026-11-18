using Foundry.Modules.Workers.Features.ContainerSpec;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>Returns a fixed <c>host.docker.internal</c> entry, standing in for the engine and WSL decision.</summary>
internal sealed class StubWorkerHostEntryResolver(string entry = "host.docker.internal:host-gateway") : IWorkerHostEntryResolver
{
    public Task<string> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(entry);
}
