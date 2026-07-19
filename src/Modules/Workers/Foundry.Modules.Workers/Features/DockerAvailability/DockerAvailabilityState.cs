namespace Foundry.Modules.Workers.Features.DockerAvailability;

internal interface IDockerAvailabilityState
{
    bool IsAvailable { get; }
}

internal sealed class DockerAvailabilityState : IDockerAvailabilityState
{
    private bool _isAvailable;

    bool IDockerAvailabilityState.IsAvailable => _isAvailable;

    public void Set(bool isAvailable)
    {
        _isAvailable = isAvailable;
    }
}
