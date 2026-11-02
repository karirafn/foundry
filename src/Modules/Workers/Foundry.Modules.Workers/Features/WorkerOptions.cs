using Foundry.Modules.Workers.Features.ContainerSpec;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerOptions
{
    public WorkerSettingsOptions Settings { get; set; } = new();
}
