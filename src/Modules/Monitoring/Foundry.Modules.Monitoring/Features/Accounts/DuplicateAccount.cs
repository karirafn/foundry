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

            return (entry.Value.HolderName, entry.Key);
        }

        return null;
    }
}
