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
