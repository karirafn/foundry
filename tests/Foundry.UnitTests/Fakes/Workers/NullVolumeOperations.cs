using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// No-op implementation of <see cref="IVolumeOperations"/> for tests that do not exercise volume behavior.
/// </summary>
internal sealed class NullVolumeOperations : IVolumeOperations
{
    public Task<VolumeResponse> CreateAsync(
        VolumesCreateParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new VolumeResponse { Name = parameters.Name });

    public Task<VolumeResponse> InspectAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(new VolumeResponse { Name = name });

    public Task<VolumesListResponse> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult(new VolumesListResponse());

    public Task<VolumesListResponse> ListAsync(
        VolumesListParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new VolumesListResponse());

    public Task<VolumesPruneResponse> PruneAsync(
        VolumesPruneParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new VolumesPruneResponse());

    public Task RemoveAsync(string name, bool? force, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
