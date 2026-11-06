namespace Foundry.Modules.Settings.Contracts;

public sealed record ClaudeCodeSettingsSnapshot(bool BlockCiCdEdits, IReadOnlyList<string> AdditionalDenyRules);
