using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Credentials.Infrastructure;

/// <summary>
/// Constants for the Docker credential volume that holds Claude authentication state.
/// </summary>
internal static class CredentialVolumeConstants
{
    internal const string VolumeName = WorkerVolumeNames.CredentialVolumeName;
    internal const string ContainerPath = WorkerVolumeNames.ClaudeConfigContainerPath;
    internal const string ConfigDirEnvVar = WorkerVolumeNames.ClaudeConfigDirEnvVar;
}
