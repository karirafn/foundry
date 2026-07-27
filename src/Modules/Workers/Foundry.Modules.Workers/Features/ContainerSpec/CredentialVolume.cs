using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal static class CredentialVolume
{
    internal const string VolumeName = WorkerVolumeNames.CredentialVolumeName;
    internal const string ContainerPath = WorkerVolumeNames.ClaudeConfigContainerPath;
    internal const string ConfigDirEnvVar = WorkerVolumeNames.ClaudeConfigDirEnvVar;
}
