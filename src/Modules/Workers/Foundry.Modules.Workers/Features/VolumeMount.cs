namespace Foundry.Modules.Workers.Features;

internal sealed record VolumeMount(string VolumeName, string ContainerPath, bool ReadOnly = false);
