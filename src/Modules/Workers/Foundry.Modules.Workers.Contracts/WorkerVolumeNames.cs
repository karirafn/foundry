namespace Foundry.Modules.Workers.Contracts;

public static class WorkerVolumeNames
{
    public const string CredentialVolumeName = "foundry-claude-credentials";
    public const string ClaudeConfigContainerPath = "/home/node/.claude";
    public const string ClaudeConfigDirEnvVar = "CLAUDE_CONFIG_DIR";

    public const string DataRootVolumeNamePrefix = "foundry-worker-docker-";
    public const string DataRootContainerPath = "/home/node/.local/share/docker";

    public static string DataRootVolumeName(WorkerRunId runId) => $"{DataRootVolumeNamePrefix}{runId.Value}";
}
