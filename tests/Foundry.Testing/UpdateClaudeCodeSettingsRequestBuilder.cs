namespace Foundry.Testing;

/// <summary>
/// Test data builder for the <c>PUT /api/settings/claude-code</c> request body.
/// Defaults to a valid body: no additional deny rules.
/// </summary>
public sealed class UpdateClaudeCodeSettingsRequestBuilder
{
    private readonly List<string> _additionalDenyRules = [];

    public UpdateClaudeCodeSettingsRequestBuilder WithAdditionalDenyRule(string rule)
    {
        _additionalDenyRules.Add(rule);
        return this;
    }

    public object Build() => new
    {
        additionalDenyRules = _additionalDenyRules.ToArray(),
    };
}
