namespace Foundry.Modules.Settings.Contracts;

public sealed record ClaudeCodeSettingsSnapshot(IReadOnlyList<string> AdditionalDenyRules);
