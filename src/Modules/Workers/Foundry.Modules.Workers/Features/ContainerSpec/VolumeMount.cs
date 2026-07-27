namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal sealed record VolumeMount(string VolumeName, string ContainerPath, bool ReadOnly = false);
