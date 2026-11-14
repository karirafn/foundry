using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public abstract class Credential : AggregateRoot<CredentialId>
{
    /// <summary>
    /// Minimum interval between automatic rechecks of an <see cref="TokenValidity.Invalid"/> account
    /// token. One recheck per interval per account keeps a dead token from tripping the provider's
    /// failed-login lockout for sibling accounts with the same login.
    /// </summary>
    public static readonly TimeSpan TokenRecheckCooldown = TimeSpan.FromMinutes(15);

    private readonly List<CredentialNamespace> _namespaces = [];

    // Both null means Valid; persisted as token_invalid_since / token_checked_at.
    private DateTimeOffset? _tokenInvalidSince;
    private DateTimeOffset? _tokenCheckedAt;

    protected Credential(CredentialId id) : base(id)
    {
    }

    public string Name { get; private protected set; } = string.Empty;

    public ProviderToken? Token { get; private set; }

    /// <summary>
    /// Incremented each time the account token is set. A concurrency token, so a verdict about a
    /// token that was replaced while it was being checked is never saved against the new one.
    /// </summary>
    public int TokenVersion { get; private set; }

    /// <summary>The provider's verdict on the account token.</summary>
    public TokenValidity TokenValidity => _tokenInvalidSince is { } detectedAt
        ? new TokenValidity.Invalid(detectedAt, _tokenCheckedAt ?? detectedAt)
        : new TokenValidity.Valid();

    /// <summary>
    /// Records that the provider refused the account token at <paramref name="now"/>. An account that is
    /// already Invalid keeps its first detection time and only stamps the check time; only the move from
    /// Valid raises <see cref="CredentialTokenInvalidated"/>.
    /// </summary>
    public void MarkTokenInvalid(DateTimeOffset now)
    {
        bool wasValid = _tokenInvalidSince is null;
        _tokenInvalidSince ??= now;
        _tokenCheckedAt = now;

        if (wasValid)
        {
            AddDomainEvent(new CredentialTokenInvalidated(Id));
        }
    }

    /// <summary>
    /// Records that the provider accepts the account token. Only the move from Invalid raises
    /// <see cref="CredentialTokenRevalidated"/>.
    /// </summary>
    public void MarkTokenValid()
    {
        bool wasInvalid = _tokenInvalidSince is not null;
        _tokenInvalidSince = null;
        _tokenCheckedAt = null;

        if (wasInvalid)
        {
            AddDomainEvent(new CredentialTokenRevalidated(Id));
        }
    }

    /// <summary>
    /// Whether an Invalid account token is due for a recheck: at least <see cref="TokenRecheckCooldown"/>
    /// has passed since it was last checked. A Valid token is never due.
    /// </summary>
    public bool IsDueForTokenRecheck(DateTimeOffset now) =>
        TokenValidity is TokenValidity.Invalid invalid && now - invalid.LastCheckedAt >= TokenRecheckCooldown;

    /// <summary>
    /// Sets a new account token. The provider has not refused a new token, so the account becomes Valid.
    /// </summary>
    private protected void SetAccountToken(string token)
    {
        Token = new ProviderToken.Present(token);
        TokenVersion++;
        MarkTokenValid();
    }

    /// <summary>
    /// Returns the raw decrypted token string when <see cref="Token"/> is <see cref="ProviderToken.Present"/>;
    /// returns <see langword="null"/> when the token is absent or unreadable.
    /// </summary>
    internal string? ReadableTokenValue => Token is ProviderToken.Present present ? present.Value : null;

    /// <summary>
    /// Optional, server-only token used solely to write default-branch protection.
    /// It is encrypted at rest exactly like <see cref="Token"/>, never returned by the API, and
    /// never given to workers — the dispatch path reads <see cref="Token"/> only.
    /// </summary>
    public ProviderToken? BranchRulesToken { get; private set; }

    /// <summary>
    /// Returns <see langword="true"/> when the stored branch-rules token could not be decrypted.
    /// Unlike <see cref="IsTokenUnreadable"/>, this never affects monitoring eligibility.
    /// </summary>
    public bool IsBranchRulesTokenUnreadable => BranchRulesToken is ProviderToken.Unreadable;

    /// <summary>Sets (or replaces, including repairing an unreadable one) the branch-rules token.</summary>
    public void SetBranchRulesToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        BranchRulesToken = new ProviderToken.Present(token);
    }

    /// <summary>Removes the branch-rules token.</summary>
    public void ClearBranchRulesToken() => BranchRulesToken = null;

    /// <summary>
    /// Marks <paramref name="credential"/> as carrying an <see cref="ProviderToken.Unreadable"/>
    /// branch-rules token. Intended for unit-test construction only — the production path is EF
    /// materialization of garbage ciphertext.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal static void ApplyUnreadableBranchRulesToken(Credential credential) =>
        credential.BranchRulesToken = new ProviderToken.Unreadable();

    public BaseUrl BaseUrl { get; private protected set; } = null!;

    public string Host { get; private protected set; } = string.Empty;

    public IReadOnlyCollection<CredentialNamespace> Namespaces => _namespaces;

    public abstract Uri ApiBaseUrl { get; }

    public void SetNamespaces(IEnumerable<Namespace> namespaces)
    {
        _namespaces.Clear();

        HashSet<string> seen = [];

        foreach (Namespace ns in namespaces)
        {
            if (seen.Add(ns.Value))
            {
                _namespaces.Add(CredentialNamespace.Create(Id, Host, ns));
            }
        }
    }

    public void SetNamespaces(IEnumerable<Namespace> derived, IReadOnlySet<string> claimedByOthers)
    {
        _namespaces.Clear();

        HashSet<string> seen = [];

        foreach (Namespace ns in derived)
        {
            if (!claimedByOthers.Contains(ns.Value) && seen.Add(ns.Value))
            {
                _namespaces.Add(CredentialNamespace.Create(Id, Host, ns));
            }
        }
    }

    /// <summary>
    /// Configures an already-constructed <paramref name="credential"/> to carry an
    /// <see cref="ProviderToken.Unreadable"/> token. Intended for unit-test construction of the
    /// <c>Unreadable</c> state only — the production path to <see cref="ProviderToken.Unreadable"/>
    /// is EF materialization of garbage ciphertext, exercised by the integration test
    /// <c>WhenAccountHasGarbageCiphertext</c> in
    /// <c>tests/Foundry.IntegrationTests/Modules/Monitoring/Endpoints/GetAccountsTests/</c>.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    protected static void ApplyUnreadableToken(Credential credential, string name, BaseUrl baseUrl)
    {
        credential.Name = name;
        credential.Token = new ProviderToken.Unreadable();
        credential.BaseUrl = baseUrl;
        credential.Host = baseUrl.Value.Host;
    }

    /// <summary>
    /// Returns <see langword="true"/> when this credential's token is <see cref="ProviderToken.Unreadable"/> —
    /// the stored ciphertext could not be decrypted and the credential is treated as ineligible.
    /// </summary>
    public bool IsTokenUnreadable => Token is ProviderToken.Unreadable;

    /// <summary>Decides whether this account's token can be used for a provider call.</summary>
    public TokenUsability TokenUsability => Token switch
    {
        ProviderToken.Present present when TokenValidity is TokenValidity.Invalid =>
            new TokenUsability.Invalid(present.Value),
        ProviderToken.Present present => new TokenUsability.Usable(present.Value),
        ProviderToken.Unreadable => new TokenUsability.Unreadable(),
        null => new TokenUsability.Absent(),
        _ => throw new UnreachableException($"Unhandled ProviderToken variant: {Token.GetType().Name}"),
    };

    internal bool Covers(RepositorySlug slug) => ResolveCoveringNamespace(slug) is not null;

    internal Namespace? ResolveCoveringNamespace(RepositorySlug slug)
    {
        HashSet<string> ownedValues = _namespaces
            .Select(n => n.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (Namespace candidate in Namespace.PrefixesOf(slug))
        {
            if (ownedValues.Contains(candidate.Value))
            {
                return candidate;
            }
        }

        return null;
    }
}
