using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class GetAvailableRepositories
{
    internal sealed record Query(Guid AccountId) : IQuery<AvailableRepositoriesResponse>;

    internal sealed class Handler(
        DbContext dbContext,
        GitHubHttpClient gitHubHttpClient,
        GitLabHttpClient gitLabHttpClient)
        : IQueryHandler<Query, AvailableRepositoriesResponse>
    {
        public async Task<Result<AvailableRepositoriesResponse>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            CredentialId credentialId = CredentialId.From(query.AccountId);

            Credential? credential = await dbContext.Set<Credential>()
                .AsNoTracking()
                .Include(c => c.Namespaces)
                .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

            if (credential is null)
            {
                return Result<AvailableRepositoriesResponse>.Fail(
                    RepositoryErrors.AccountNotFound(credentialId));
            }

            if (credential.Token is null)
            {
                return Result<AvailableRepositoriesResponse>.Fail(
                    RepositoryErrors.AccountHasNoToken(credentialId));
            }

            Result<IReadOnlyList<ProviderRepository>> providerResult = credential switch
            {
                GitLabCredential => await gitLabHttpClient.ListRepositoriesAsync(
                    credential.ApiBaseUrl,
                    credential.Token,
                    cancellationToken),
                _ => await gitHubHttpClient.ListRepositoriesAsync(
                    credential.ApiBaseUrl,
                    credential.Token,
                    cancellationToken),
            };

            if (providerResult is not Result<IReadOnlyList<ProviderRepository>>.Success providerSuccess)
            {
                return Result<AvailableRepositoriesResponse>.Fail(
                    ((Result<IReadOnlyList<ProviderRepository>>.Failure)providerResult).Error);
            }

            IReadOnlyList<ProviderRepository> providerRepos = providerSuccess.Value;

            HashSet<string> monitoredSlugs = await LoadMonitoredSlugsAsync(credential.Host, cancellationToken);

            bool hasClaims = credential.Namespaces.Count > 0;

            IReadOnlyList<Namespace> claims = credential.Namespaces
                .Select(n => Namespace.Create(n.Value))
                .OfType<Result<Namespace>.Success>()
                .Select(r => r.Value)
                .ToList();

            List<AvailableRepository> repositories = [];

            foreach (ProviderRepository providerRepo in providerRepos)
            {
                Result<RepositorySlug> slugResult = RepositorySlug.Create(providerRepo.Slug);

                if (slugResult is not Result<RepositorySlug>.Success slugSuccess)
                {
                    continue;
                }

                RepositorySlug slug = slugSuccess.Value;

                if (!claims.Any(n => n.IsPrefixOf(slug)))
                {
                    continue;
                }

                bool isMonitored = monitoredSlugs.Contains(slug.FullPath);

                repositories.Add(new AvailableRepository(
                    slug.FullPath,
                    providerRepo.IsPrivate,
                    providerRepo.CanPush,
                    isMonitored));
            }

            return Result<AvailableRepositoriesResponse>.Ok(new AvailableRepositoriesResponse(hasClaims, repositories));
        }

        private async Task<HashSet<string>> LoadMonitoredSlugsAsync(string host, CancellationToken cancellationToken)
        {
            List<string> slugs = await dbContext.Set<MonitoredRepository>()
                .AsNoTracking()
                .Where(r => r.Host == host)
                .Select(r => r.Slug.FullPath)
                .ToListAsync(cancellationToken);

            return slugs.ToHashSet(StringComparer.Ordinal);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("available-repositories", static async (
                    Guid accountId,
                    IQueryHandler<Query, AvailableRepositoriesResponse> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<AvailableRepositoriesResponse> result = await handler.HandleAsync(
                        new Query(accountId),
                        cancellationToken);

                    return result.Match<Results<Ok<AvailableRepositoriesResponse>, NotFound<string>, BadRequest<string>>>(
                        response => TypedResults.Ok(response),
                        error => error.Code switch
                        {
                            RepositoryErrors.AccountNotFoundCode => TypedResults.NotFound(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("GetAvailableRepositories")
                .WithSummary("Gets all repositories available for a given account")
                .Produces<AvailableRepositoriesResponse>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
