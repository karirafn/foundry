using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Lists every Package Source active monitored repositories declare, sorted by URL, with its declaring repositories,
/// its status (<see cref="PackageSourceStatusRule"/>) and its registered credential's username. A source no active
/// repository declares is left out; its stored credential is kept.
/// </summary>
internal static class GetPackageSources
{
    internal sealed class Handler(DbContext dbContext, ICredentialResolver credentialResolver)
    {
        public async Task<IReadOnlyList<PackageSourceSummary>> HandleAsync(CancellationToken cancellationToken)
        {
            List<MonitoredRepository> repositories = await dbContext.Set<MonitoredRepository>()
                .AsNoTracking()
                .Where(r => r.IsActive)
                .ToListAsync(cancellationToken);

            Dictionary<string, PackageSource> sources = (await dbContext.Set<PackageSource>()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken))
                .ToDictionary(s => s.ServiceIndexUrl.AbsoluteUri, StringComparer.Ordinal);

            Dictionary<string, List<Declarer>> declarers = new(StringComparer.Ordinal);
            foreach (MonitoredRepository repository in repositories.Where(r => r.DeclaredPackageSources.Count > 0))
            {
                Credential? account = await credentialResolver.ResolveAsync(
                    repository.Host,
                    repository.Slug,
                    cancellationToken);

                foreach (DeclaredPackageSource declared in repository.DeclaredPackageSources)
                {
                    string url = declared.ServiceIndexUrl.AbsoluteUri;
                    if (!declarers.TryGetValue(url, out List<Declarer>? list))
                    {
                        list = [];
                        declarers.Add(url, list);
                    }

                    list.Add(new Declarer(repository, Fallback(declared.ServiceIndexUrl, repository.Host, account)));
                }
            }

            List<PackageSourceSummary> summaries = [];
            foreach ((string url, List<Declarer> list) in declarers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                // A source the probe has not stored yet has no id to register a credential against; the next probe
                // pass stores it.
                if (!sources.TryGetValue(url, out PackageSource? source))
                {
                    continue;
                }

                ListedStatus status = PackageSourceStatusRule.For(
                    source,
                    list.Select(d => d.Fallback).OfType<FallbackRead>());

                summaries.Add(new PackageSourceSummary(
                    source.Id.Value,
                    url,
                    list.Select(d => new PackageSourceRepository(d.Repository.Id.Value, d.Repository.Slug.ToString()))
                        .DistinctBy(r => r.Id)
                        .OrderBy(r => r.Slug, StringComparer.Ordinal)
                        .ToList(),
                    status.Status,
                    status.LastCheckedAt,
                    source.Credential is { } credential
                        ? new PackageSourceCredentialSummary(credential.Username, credential.UpdatedAt)
                        : null));
            }

            return summaries;
        }

        /// <summary>
        /// The credential the repository reads the source with when none is registered; null when its account cannot
        /// be used, because the source is then never probed for it (its eligibility stops at the account).
        /// </summary>
        private static FallbackRead? Fallback(Uri sourceUrl, string repositoryHost, Credential? account)
        {
            if (account?.TokenUsability is not TokenUsability.Usable)
            {
                return null;
            }

            return PackageSourceAccess.UsesAccount(sourceUrl, repositoryHost)
                ? new FallbackRead.ViaAccount(account.Id, account.Name.Value, account.Host)
                : new FallbackRead.Anonymous();
        }

        private sealed record Declarer(MonitoredRepository Repository, FallbackRead? Fallback);
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet(string.Empty, static async (Handler handler, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await handler.HandleAsync(cancellationToken)))
                .WithName("GetPackageSources")
                .WithSummary("Lists the package sources monitored repositories declare")
                .Produces<IReadOnlyList<PackageSourceSummary>>();
        }
    }
}
