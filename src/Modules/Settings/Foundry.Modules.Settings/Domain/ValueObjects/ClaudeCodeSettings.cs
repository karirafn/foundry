using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

/// <summary>
/// The Claude Code policy applied to every worker. The model is not here: it is a per-role setting (ADR 0095).
/// </summary>
public sealed partial record ClaudeCodeSettings
{
    private const string DenyRulePattern = @"^(Bash|Edit|Read|Write|MultiEdit|Glob|Grep)\(.+\)\z";

    [JsonConstructor]
    private ClaudeCodeSettings(IReadOnlyList<string>? additionalDenyRules = null)
    {
        AdditionalDenyRules = additionalDenyRules ?? [];
    }

    public static readonly ClaudeCodeSettings Default = Create(additionalDenyRules: [])
        .Match(
            onSuccess: v => v,
            onFailure: e => throw new InvalidOperationException($"ClaudeCodeSettings.Default is invalid: {e.Message}"));

    public IReadOnlyList<string> AdditionalDenyRules { get; }

    public static Result<ClaudeCodeSettings> Create(IReadOnlyList<string> additionalDenyRules)
    {
        HashSet<string> seenRules = new(StringComparer.Ordinal);
        List<string> canonicalRules = [];

        foreach (string rule in additionalDenyRules)
        {
            if (!DenyRuleRegex().IsMatch(rule))
            {
                return Result<ClaudeCodeSettings>.Fail(SettingsErrors.InvalidDenyRule(rule));
            }

            if (seenRules.Add(rule))
            {
                canonicalRules.Add(rule);
            }
        }

        return Result<ClaudeCodeSettings>.Ok(new ClaudeCodeSettings(canonicalRules.AsReadOnly()));
    }

    public bool Equals(ClaudeCodeSettings? other) =>
        other is not null
        && AdditionalDenyRules.SequenceEqual(other.AdditionalDenyRules);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (string rule in AdditionalDenyRules)
        {
            hash.Add(rule);
        }

        return hash.ToHashCode();
    }

    [GeneratedRegex(DenyRulePattern, RegexOptions.CultureInvariant)]
    private static partial Regex DenyRuleRegex();
}
