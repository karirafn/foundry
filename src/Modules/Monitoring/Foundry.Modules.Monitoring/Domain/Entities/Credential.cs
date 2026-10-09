using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public abstract class Credential : AggregateRoot<CredentialId>
{
    private readonly List<CredentialNamespace> _namespaces = [];

    protected Credential(CredentialId id) : base(id)
    {
    }

    public string Name { get; private protected set; } = string.Empty;

    public ProviderToken? Token { get; private protected set; }

    /// <summary>
    /// Returns the raw decrypted token string when <see cref="Token"/> is <see cref="ProviderToken.Present"/>;
    /// returns <see langword="null"/> when the token is absent or unreadable.
    /// </summary>
    internal string? ReadableTokenValue => Token is ProviderToken.Present present ? present.Value : null;

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
