namespace Foundry.Modules.Workers.Features.Roles.Models;

/// <summary>
/// The documented Claude Code model aliases. The CLI resolves an alias to a concrete model at run time, so an alias
/// tracks the newest model of its family without a settings change.
/// </summary>
internal static class ClaudeModelAliases
{
    public static readonly IReadOnlyList<string> All = ["fable", "opus", "sonnet", "haiku", "opus[1m]", "sonnet[1m]"];

    public static bool IsAlias(string model) => All.Contains(model, StringComparer.Ordinal);
}
