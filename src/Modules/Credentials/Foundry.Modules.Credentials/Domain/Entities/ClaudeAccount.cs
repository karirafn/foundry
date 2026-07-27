using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain.Entities;

public sealed class ClaudeAccount : AggregateRoot<ClaudeAccountId>
{
    internal const int MaxOAuthAccountEmailLength = 254;
    internal const int MaxOAuthAccountOrgNameLength = 200;

    private ClaudeAccount() : base(ClaudeAccountId.Default)
    {
    }

    private ClaudeAccount(ClaudeAccountId id, DateTimeOffset createdAt) : base(id)
    {
        AuthMode = new AuthMode.ApiKey(string.Empty);
        Validity = new CredentialValidity.Valid();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public AuthMode AuthMode { get; private set; } = null!;

    public CredentialValidity Validity { get; private set; } = null!;

    public string? OAuthAccountEmail { get; private set; }

    public string? OAuthAccountOrgName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ClaudeAccount Create()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ClaudeAccount(ClaudeAccountId.Default, now);
    }

    /// <summary>
    /// Sets the auth mode. When switching to <see cref="AuthMode.ApiKey"/>, clears OAuth identity
    /// fields and sets validity to <see cref="CredentialValidity.Valid"/>.
    /// </summary>
    public void SetAuthMode(AuthMode mode)
    {
        AuthMode = mode;

        if (mode is AuthMode.ApiKey)
        {
            OAuthAccountEmail = null;
            OAuthAccountOrgName = null;
            Validity = new CredentialValidity.Valid();
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the credentials as invalid with the given reason.
    /// Idempotent: when already <see cref="CredentialValidity.Invalid"/>, does nothing and returns
    /// <c>false</c> so callers can avoid double-publishing an event.
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
    /// length caps), and sets validity to <see cref="CredentialValidity.Valid"/>.
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
        AuthMode = new AuthMode.OAuth(subscriptionType);
        Validity = new CredentialValidity.Valid();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
