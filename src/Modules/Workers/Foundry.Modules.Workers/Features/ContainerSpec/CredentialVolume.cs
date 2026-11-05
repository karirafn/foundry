using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// How an OAuth worker shares the Claude credentials (ADR 0090). The credential volume is mounted at a
/// dedicated path and only the CLI's credential store is pointed at it; the worker's Claude config dir
/// (<c>CLAUDE.md</c>, skills, agents, commands, <c>settings.json</c>, <c>.claude.json</c>) stays at the
/// container-local default <c>~/.claude</c>, so nothing one worker writes there reaches another.
/// </summary>
internal static class CredentialVolume
{
    internal const string VolumeName = WorkerVolumeNames.CredentialVolumeName;
    internal const string ContainerPath = WorkerVolumeNames.WorkerCredentialContainerPath;
    internal const string SecureStorageDirEnvVar = WorkerVolumeNames.ClaudeSecureStorageDirEnvVar;
}
