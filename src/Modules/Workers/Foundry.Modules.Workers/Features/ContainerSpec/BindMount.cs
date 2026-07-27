namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal sealed record BindMount(string HostPath, string ContainerPath, bool ReadOnly = false);
