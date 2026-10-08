using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

namespace Foundry.Testing;

/// <summary>
/// Test data builder for <see cref="ClaudeAccount"/>. Reaches all states through the same
/// production transitions the aggregate exposes — no direct state stamping.
/// </summary>
public sealed class ClaudeAccountBuilder
{
    private AuthMode _authMode = new AuthMode.ApiKey(new ApiKeyCredential.NotConfigured());
    private CredentialValidity? _invalidValidity;
    private SpendState.Blocked? _blocked;
    private string? _oauthEmail;
    private string? _oauthOrgName;
    private string? _subscriptionType;

    /// <summary>Seeds an API-key account with a <see cref="ApiKeyCredential.Present"/> credential.</summary>
    public ClaudeAccountBuilder WithApiKey(string value = "sk-ant-test")
    {
        _authMode = new AuthMode.ApiKey(new ApiKeyCredential.Present(value));
        return this;
    }

    /// <summary>Seeds an API-key account with a <see cref="ApiKeyCredential.NotConfigured"/> credential.</summary>
    public ClaudeAccountBuilder WithNotConfiguredApiKey()
    {
        _authMode = new AuthMode.ApiKey(new ApiKeyCredential.NotConfigured());
        return this;
    }

    /// <summary>Seeds an OAuth account with the given identity fields.</summary>
    public ClaudeAccountBuilder WithOAuth(
        string email = "user@example.com",
        string orgName = "MyOrg",
        string? subscriptionType = "pro")
    {
        _oauthEmail = email;
        _oauthOrgName = orgName;
        _subscriptionType = subscriptionType;
        _authMode = new AuthMode.OAuth(subscriptionType);
        return this;
    }

    /// <summary>Marks the account as <see cref="CredentialValidity.Invalid"/>.</summary>
    public ClaudeAccountBuilder WithInvalidValidity(string reason = "worker_auth_failed")
    {
        _invalidValidity = new CredentialValidity.Invalid(reason);
        return this;
    }

    /// <summary>Marks spend as <see cref="SpendState.Blocked"/>.</summary>
    public ClaudeAccountBuilder WithSpendBlocked(DateTimeOffset? nextProbeAt = null)
    {
        _blocked = new SpendState.Blocked(nextProbeAt ?? DateTimeOffset.UtcNow.AddHours(1));
        return this;
    }

    public ClaudeAccount Build()
    {
        ClaudeAccount account = ClaudeAccount.Create();

        // Replay transitions in the order production code would.
        if (_authMode is AuthMode.OAuth)
        {
            account.RecordSuccessfulLogin(_oauthEmail, _oauthOrgName, _subscriptionType);
        }
        else
        {
            account.SetAuthMode(_authMode);
        }

        if (_invalidValidity is CredentialValidity.Invalid invalid)
        {
            account.Invalidate(invalid.Reason);
        }

        if (_blocked is SpendState.Blocked blocked)
        {
            account.BlockSpend(blocked.NextProbeAt);
        }

        return account;
    }
}
