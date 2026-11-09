using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// Test data builder for <see cref="ClaudeCodeSettings"/>. Defaults to the production defaults;
/// <see cref="Build"/> returns the <see cref="Result{T}"/> so tests can also exercise rejected values.
/// </summary>
public sealed class ClaudeCodeSettingsBuilder
{
    private List<string> _additionalDenyRules = [];

    public ClaudeCodeSettingsBuilder WithAdditionalDenyRules(IEnumerable<string> additionalDenyRules)
    {
        _additionalDenyRules = [.. additionalDenyRules];
        return this;
    }

    public Result<ClaudeCodeSettings> Build() =>
        ClaudeCodeSettings.Create(_additionalDenyRules);
}
