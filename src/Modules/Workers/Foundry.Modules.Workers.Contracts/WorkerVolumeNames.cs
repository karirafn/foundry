namespace Foundry.Modules.Workers.Contracts;

public static class WorkerVolumeNames
{
    public const string CredentialVolumeName = "foundry-claude-credentials";
    public const string ClaudeConfigContainerPath = "/home/node/.claude";
    public const string ClaudeConfigDirEnvVar = "CLAUDE_CONFIG_DIR";

    /// <summary>
    /// Where a worker mounts the credential volume: a dedicated directory outside its Claude config dir,
    /// so the worker's config dir stays container-local and only the OAuth credentials are shared.
    /// </summary>
    public const string WorkerCredentialContainerPath = "/home/node/.claude-credentials";

    /// <summary>
    /// Points the Claude Code CLI's credential store (<c>.credentials.json</c> and its refresh lock) at a
    /// directory other than <see cref="ClaudeConfigDirEnvVar"/>.
    /// </summary>
    public const string ClaudeSecureStorageDirEnvVar = "CLAUDE_SECURESTORAGE_CONFIG_DIR";

    public const string DataRootVolumeNamePrefix = "foundry-worker-docker-";
    public const string DataRootContainerPath = "/home/node/.local/share/docker";

    public static string DataRootVolumeName(WorkerRunId runId) => $"{DataRootVolumeNamePrefix}{runId.Value}";
}
