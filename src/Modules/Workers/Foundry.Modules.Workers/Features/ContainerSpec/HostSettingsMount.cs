namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// The contract between dispatch and the worker entrypoint for host hooks: the operator's host Claude Code
/// settings file is bind-mounted read-only at a fixed staging path (never <c>~/.claude/settings.json</c>, which
/// the entrypoint generates), and the entrypoint merges its <c>hooks</c> minus the excluded commands.
/// </summary>
internal static class HostSettingsMount
{
    internal const string ContainerPath = "/run/foundry/host-settings.json";

    internal const string FileEnvironmentVariable = "FOUNDRY_HOST_SETTINGS_FILE";

    internal const string ExclusionsEnvironmentVariable = "FOUNDRY_HOST_HOOK_EXCLUSIONS";
}
