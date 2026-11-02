namespace Foundry.Testing;

/// <summary>
/// Test data builder for the <c>PUT /api/settings/claude-code</c> request body.
/// Defaults to a valid body: empty model, CI/CD edits blocked, no additional deny rules.
/// </summary>
public sealed class UpdateClaudeCodeSettingsRequestBuilder
{
    private readonly List<string> _additionalDenyRules = [];
    private string _model = string.Empty;
    private bool _blockCiCdEdits = true;

    public UpdateClaudeCodeSettingsRequestBuilder WithModel(string model)
    {
        _model = model;
        return this;
    }

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
        model = _model,
        blockCiCdEdits = _blockCiCdEdits,
        additionalDenyRules = _additionalDenyRules.ToArray(),
    };
}
