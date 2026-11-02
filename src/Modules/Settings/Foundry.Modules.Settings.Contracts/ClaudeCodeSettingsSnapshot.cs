namespace Foundry.Modules.Settings.Contracts;

public sealed record ClaudeCodeSettingsSnapshot(string Model, bool BlockCiCdEdits, IReadOnlyList<string> AdditionalDenyRules);
