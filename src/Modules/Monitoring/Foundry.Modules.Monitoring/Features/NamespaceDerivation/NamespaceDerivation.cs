using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.NamespaceDerivation;

internal static class NamespaceDerivation
{
    /// <summary>
    /// Derives the set of owner namespaces from a writable-repository listing.
    /// For each repository where <see cref="ProviderRepository.CanPush"/> is true,
    /// extracts the owner (all slug segments except the last) and de-duplicates.
    /// </summary>
    public static IReadOnlyCollection<Namespace> FromWritableRepositories(
        IEnumerable<ProviderRepository> repositories)
    {
        HashSet<string> seen = [];
        List<Namespace> namespaces = [];

        foreach (ProviderRepository repo in repositories)
        {
            if (!repo.CanPush)
            {
                continue;
            }

            string slug = repo.Slug;
            int lastSlash = slug.LastIndexOf('/');

            if (lastSlash <= 0)
            {
                continue;
            }

            string owner = slug[..lastSlash];

            if (!seen.Add(owner))
            {
                continue;
            }

            if (Namespace.Create(owner) is not Result<Namespace>.Success { Value: Namespace ns })
            {
                continue;
            }

            namespaces.Add(ns);
        }

        return namespaces;
    }
}
