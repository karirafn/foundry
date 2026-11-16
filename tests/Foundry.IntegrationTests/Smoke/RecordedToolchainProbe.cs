using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ImageBuild;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>A toolchain probe with no Docker behind it: every image reports the same two tools.</summary>
internal sealed class RecordedToolchainProbe : IWorkerImageToolchainProbe
{
    public Task<Toolchain?> ProbeAsync(string imageId, CancellationToken cancellationToken) =>
        Task.FromResult(Toolchain.TryCreate([Entry("node", "22.22.3"), Entry("git", "2.39.5")]));

    private static ToolchainEntry Entry(string name, string version) =>
        ToolchainEntry.TryCreate(name, version)
            ?? throw new InvalidOperationException($"The probe entry {name} must be valid.");
}
