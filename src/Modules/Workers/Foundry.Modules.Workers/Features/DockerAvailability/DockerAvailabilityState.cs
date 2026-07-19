namespace Foundry.Modules.Workers.Features.DockerAvailability;

internal interface IDockerAvailabilityState
{
    bool IsAvailable { get; }
}

internal interface IDockerAvailabilityStateMutator
{
    void Set(bool isAvailable);
}

internal sealed class DockerAvailabilityState : IDockerAvailabilityState, IDockerAvailabilityStateMutator
{
    private volatile bool _isAvailable;

    bool IDockerAvailabilityState.IsAvailable => _isAvailable;

    public void Set(bool isAvailable)
    {
        _isAvailable = isAvailable;
    }
}
