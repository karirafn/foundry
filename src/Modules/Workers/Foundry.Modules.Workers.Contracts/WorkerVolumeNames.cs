namespace Foundry.Modules.Workers.Contracts;

public static class WorkerVolumeNames
{
    public const string CredentialVolumeName = "foundry-claude-credentials";
    public const string ClaudeConfigContainerPath = "/home/node/.claude";
    public const string ClaudeConfigDirEnvVar = "CLAUDE_CONFIG_DIR";
}
