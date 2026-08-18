using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class DuplicateAccount
{
    /// <summary>
    /// Returns the first claim where another credential shares the same <paramref name="resolvedName"/>
    /// and its claimed namespace intersects the <paramref name="derivedNamespaces"/> set.
    /// Returns <c>null</c> when no such claim exists — including the legitimate case of
    /// the same login covering distinct owner namespaces.
    /// </summary>
    internal static (string HolderName, string SharedOwner)? Find(
        string resolvedName,
        IReadOnlyCollection<Namespace> derivedNamespaces,
        IReadOnlyDictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers)
    {
        HashSet<string> derivedValues = new(
            derivedNamespaces.Select(ns => ns.Value),
            StringComparer.Ordinal);

        string? earliestNamespace = null;
        string? holderName = null;

        foreach (KeyValuePair<string, (Guid HolderCredentialId, string HolderName)> entry in claimedByOthers)
        {
            if (!string.Equals(entry.Value.HolderName, resolvedName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!derivedValues.Contains(entry.Key))
            {
                continue;
            }

            if (earliestNamespace is null || StringComparer.Ordinal.Compare(entry.Key, earliestNamespace) < 0)
            {
                earliestNamespace = entry.Key;
                holderName = entry.Value.HolderName;
            }
        }

        return earliestNamespace is null ? null : (holderName!, earliestNamespace);
    }
}
