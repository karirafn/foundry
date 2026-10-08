using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain.Entities;

public sealed class ClaudeAccount : AggregateRoot<ClaudeAccountId>
{
    internal const int MaxOAuthAccountEmailLength = 254;
    internal const int MaxOAuthAccountOrgNameLength = 200;

    private AuthMode _authModeRecord = null!;
    private ApiKeyCredential? _apiKeyCredential;

    private ClaudeAccount() : base(ClaudeAccountId.Default)
    {
    }

    private ClaudeAccount(ClaudeAccountId id, DateTimeOffset createdAt) : base(id)
    {
        _authModeRecord = new AuthMode.ApiKey(new ApiKeyCredential.NotConfigured());
        _apiKeyCredential = new ApiKeyCredential.NotConfigured();
        Validity = new CredentialValidity.Valid();
        SpendState = new SpendState.Available();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// Assembles the current auth mode from the private mode record and the separately-stored
    /// API key credential. <c>_authModeRecord</c> carries the discriminator (ApiKey or OAuth);
    /// <c>_apiKeyCredential</c> carries the key credential loaded from the separate
    /// <c>api_key</c> column. For non-EF-loaded instances (e.g. freshly created), the credential
    /// in <c>apiKeyRecord</c> is used as a fallback.
    /// </summary>
    public AuthMode AuthMode
    {
        get
        {
            if (_authModeRecord is not AuthMode.ApiKey apiKeyRecord)
            {
                return _authModeRecord;
            }

            // Prefer the separately-loaded credential field; fall back to the credential embedded
            // in the mode record for in-memory instances not yet persisted.
            ApiKeyCredential credential = _apiKeyCredential ?? apiKeyRecord.Credential;
            return new AuthMode.ApiKey(credential);
        }
    }

    public CredentialValidity Validity { get; private set; } = null!;

    public SpendState SpendState { get; private set; } = null!;

    public string? OAuthAccountEmail { get; private set; }

    public string? OAuthAccountOrgName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Returns false when validity is Invalid, spend is Blocked, or mode is API-key
    /// with a non-Present credential. Returns true otherwise. No DB access.
    /// </summary>
    public bool CanDispatch
    {
        get
        {
            if (Validity is CredentialValidity.Invalid)
            {
                return false;
            }

            if (SpendState is SpendState.Blocked)
            {
                return false;
            }

            if (_authModeRecord is AuthMode.ApiKey && _apiKeyCredential is not ApiKeyCredential.Present)
            {
                return false;
            }

            return true;
        }
    }

    public static ClaudeAccount Create()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ClaudeAccount(ClaudeAccountId.Default, now);
    }

    /// <summary>
    /// Sets the auth mode. When switching to <see cref="AuthMode.ApiKey"/>, clears OAuth identity
    /// fields, sets validity to <see cref="CredentialValidity.Valid"/>, and stores the credential.
    /// Switching to OAuth clears the stored API key credential.
    /// </summary>
    public void SetAuthMode(AuthMode mode)
    {
        switch (mode)
        {
            case AuthMode.ApiKey apiKey:
                _apiKeyCredential = apiKey.Credential;
                _authModeRecord = new AuthMode.ApiKey(apiKey.Credential);
                OAuthAccountEmail = null;
                OAuthAccountOrgName = null;
                Validity = new CredentialValidity.Valid();
                break;

            case AuthMode.OAuth:
                _authModeRecord = mode;
                _apiKeyCredential = null;
                break;

            default:
                _authModeRecord = mode;
                break;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the credentials as invalid with the given reason.
    /// Idempotent: when already <see cref="CredentialValidity.Invalid"/>, does nothing and returns
    /// <c>false</c> so callers can avoid double-publishing an event.
    /// Does not affect <see cref="Validity"/>.
    /// </summary>
    /// <returns><c>true</c> if the state changed; <c>false</c> if already invalid.</returns>
    public bool Invalidate(string reason)
    {
        if (Validity is CredentialValidity.Invalid)
        {
            return false;
        }

        Validity = new CredentialValidity.Invalid(reason);
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Records a successful OAuth login: sets the OAuth auth mode, writes the identity (clamped to
    /// length caps), clears the API key credential, and sets validity to <see cref="CredentialValidity.Valid"/>.
    /// </summary>
    public void RecordSuccessfulLogin(string? email, string? orgName, string? subscriptionType)
    {
        // Clamp at the domain caps to enforce the invariant regardless of caller.
        // SQLite does not enforce HasMaxLength, so a field exceeding the cap would persist
        // but fail a future migration to a stricter database engine.
        OAuthAccountEmail = email is not null && email.Length > MaxOAuthAccountEmailLength
            ? email[..MaxOAuthAccountEmailLength]
            : email;
        OAuthAccountOrgName = orgName is not null && orgName.Length > MaxOAuthAccountOrgNameLength
            ? orgName[..MaxOAuthAccountOrgNameLength]
            : orgName;
        _authModeRecord = new AuthMode.OAuth(subscriptionType);
        _apiKeyCredential = null;
        Validity = new CredentialValidity.Valid();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks spend as blocked with a scheduled probe time, preventing further dispatch.
    /// Idempotent: when already <see cref="SpendState.Blocked"/>, does nothing and returns
    /// <c>false</c>, preserving the existing probe schedule so a mid-probe manual resume
    /// cannot reset the arm.
    /// Does not affect <see cref="Validity"/>.
    /// </summary>
    /// <returns><c>true</c> if the state changed; <c>false</c> if already blocked.</returns>
    public bool BlockSpend(DateTimeOffset nextProbeAt)
    {
        if (SpendState is SpendState.Blocked)
        {
            return false;
        }

        SpendState = new SpendState.Blocked(nextProbeAt);
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Re-arms the probe schedule on a blocked account.
    /// Idempotent: when <see cref="SpendState.Available"/>, does nothing and returns <c>false</c>
    /// so a probe completing after a manual resume cannot re-block the account.
    /// </summary>
    /// <returns><c>true</c> if the arm was updated; <c>false</c> if not blocked.</returns>
    public bool RearmProbe(DateTimeOffset nextProbeAt)
    {
        if (SpendState is not SpendState.Blocked)
        {
            return false;
        }

        SpendState = new SpendState.Blocked(nextProbeAt);
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Restores spend to available, allowing dispatch to resume.
    /// Idempotent: when already <see cref="SpendState.Available"/>, does nothing and returns
    /// <c>false</c>.
    /// Does not affect <see cref="Validity"/>.
    /// </summary>
    /// <returns><c>true</c> if the state changed; <c>false</c> if already available.</returns>
    public bool RestoreSpend()
    {
        if (SpendState is SpendState.Available)
        {
            return false;
        }

        SpendState = new SpendState.Available();
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }
}
