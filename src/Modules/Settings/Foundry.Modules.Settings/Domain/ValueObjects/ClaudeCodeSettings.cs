using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

public sealed partial record ClaudeCodeSettings
{
    internal const int MaxModelLength = 200;

    private const string DenyRulePattern = @"^(Bash|Edit|Read|Write|MultiEdit|Glob|Grep)\(.+\)\z";

    [JsonConstructor]
    private ClaudeCodeSettings(string model, bool blockCiCdEdits, IReadOnlyList<string> additionalDenyRules)
    {
        Model = model;
        BlockCiCdEdits = blockCiCdEdits;
        AdditionalDenyRules = additionalDenyRules;
    }

    public static readonly ClaudeCodeSettings Default = Create(
        model: string.Empty,
        blockCiCdEdits: true,
        additionalDenyRules: [])
        .Match(
            onSuccess: v => v,
            onFailure: e => throw new InvalidOperationException($"ClaudeCodeSettings.Default is invalid: {e.Message}"));

    public string Model { get; }

    public bool BlockCiCdEdits { get; }

    public IReadOnlyList<string> AdditionalDenyRules { get; }

    public static Result<ClaudeCodeSettings> Create(
        string model,
        bool blockCiCdEdits,
        IReadOnlyList<string> additionalDenyRules)
    {
        string trimmedModel = model.Trim();

        if (trimmedModel.Length > MaxModelLength)
        {
            return Result<ClaudeCodeSettings>.Fail(SettingsErrors.ModelTooLong);
        }

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

        return Result<ClaudeCodeSettings>.Ok(new ClaudeCodeSettings(trimmedModel, blockCiCdEdits, canonicalRules));
    }

    public bool Equals(ClaudeCodeSettings? other) =>
        other is not null
        && Model == other.Model
        && BlockCiCdEdits == other.BlockCiCdEdits
        && AdditionalDenyRules.SequenceEqual(other.AdditionalDenyRules);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Model);
        hash.Add(BlockCiCdEdits);
        foreach (string rule in AdditionalDenyRules)
        {
            hash.Add(rule);
        }

        return hash.ToHashCode();
    }

    [GeneratedRegex(DenyRulePattern, RegexOptions.CultureInvariant)]
    private static partial Regex DenyRuleRegex();
}
