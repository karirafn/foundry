namespace Foundry.Testing;

/// <summary>
/// Test data builder for the <c>PUT /api/settings/claude-code</c> request body.
/// Defaults to a valid body: CI/CD edits blocked, no additional deny rules.
/// </summary>
public sealed class UpdateClaudeCodeSettingsRequestBuilder
{
    private readonly List<string> _additionalDenyRules = [];
    private bool _blockCiCdEdits = true;

    public UpdateClaudeCodeSettingsRequestBuilder WithBlockCiCdEdits(bool blockCiCdEdits)
    {
        _blockCiCdEdits = blockCiCdEdits;
        return this;
    }

    public UpdateClaudeCodeSettingsRequestBuilder WithAdditionalDenyRule(string rule)
    {
        _additionalDenyRules.Add(rule);
        return this;
    }

    public object Build() => new
    {
        blockCiCdEdits = _blockCiCdEdits,
        additionalDenyRules = _additionalDenyRules.ToArray(),
    };
}
