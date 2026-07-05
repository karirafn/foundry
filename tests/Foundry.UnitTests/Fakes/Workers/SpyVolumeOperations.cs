using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable spy of <see cref="IVolumeOperations"/> for unit-testing Docker volume
/// interactions with zero Docker daemon.
/// <para>
/// Inspect captured call arguments via <c>Last*</c> properties.
/// </para>
/// </summary>
internal sealed class SpyVolumeOperations : IVolumeOperations
{
    public VolumesCreateParameters? LastCreateParameters { get; private set; }

    public Task<VolumeResponse> CreateAsync(
        VolumesCreateParameters parameters,
        CancellationToken cancellationToken)
    {
        LastCreateParameters = parameters;
        return Task.FromResult(new VolumeResponse { Name = parameters.Name });
    }

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
