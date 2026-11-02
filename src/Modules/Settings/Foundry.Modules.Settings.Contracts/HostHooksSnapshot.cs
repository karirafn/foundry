namespace Foundry.Modules.Settings.Contracts;

/// <summary>
/// The host Claude Code settings file whose <c>hooks</c> the worker entrypoint imports, and the hook commands
/// it drops. An empty <see cref="HostSettingsPath"/> means no host hooks are imported.
/// </summary>
public sealed record HostHooksSnapshot(string HostSettingsPath, IReadOnlyList<string> ExcludedCommands);
