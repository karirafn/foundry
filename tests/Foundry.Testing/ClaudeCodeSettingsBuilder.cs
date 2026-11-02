using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// Test data builder for <see cref="ClaudeCodeSettings"/>. Defaults to the production defaults;
/// <see cref="Build"/> returns the <see cref="Result{T}"/> so tests can also exercise rejected values.
/// </summary>
public sealed class ClaudeCodeSettingsBuilder
{
    private string _model = string.Empty;
    private bool _blockCiCdEdits = true;
    private List<string> _additionalDenyRules = [];

    public ClaudeCodeSettingsBuilder WithModel(string model)
    {
        _model = model;
        return this;
    }

    public ClaudeCodeSettingsBuilder WithBlockCiCdEdits(bool blockCiCdEdits)
    {
        _blockCiCdEdits = blockCiCdEdits;
        return this;
    }

    public ClaudeCodeSettingsBuilder WithAdditionalDenyRules(IEnumerable<string> additionalDenyRules)
    {
        _additionalDenyRules = [.. additionalDenyRules];
        return this;
    }

    public Result<ClaudeCodeSettings> Build() =>
        ClaudeCodeSettings.Create(_model, _blockCiCdEdits, _additionalDenyRules);
}
