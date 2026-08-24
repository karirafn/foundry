using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class GetRepositories
{
    internal sealed record Query(Guid AccountId) : IQuery<IReadOnlyList<RepositorySummary>>;

    internal sealed class Handler(DbContext dbContext)
        : IQueryHandler<Query, IReadOnlyList<RepositorySummary>>
    {
        public async Task<Result<IReadOnlyList<RepositorySummary>>> HandleAsync(
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
                return Result<IReadOnlyList<RepositorySummary>>.Ok([]);
            }

            // Load all repos for the credential's host, then filter in memory to those
            // covered by at least one of the credential's namespaces.
            List<MonitoredRepository> hostRepos = await dbContext.Set<MonitoredRepository>()
                .AsNoTracking()
                .Where(r => r.Host == credential.Host)
                .OrderBy(r => r.Position)
                .ToListAsync(cancellationToken);

            string providerType = credential switch
            {
                GitHubCredential => ProviderTypes.GitHub,
                GitLabCredential => ProviderTypes.GitLab,
                _ => throw new UnreachableException(),
            };

            List<RepositorySummary> repositories = hostRepos
                .Where(r => credential.ResolveCoveringNamespace(r.Slug) is not null)
                .Select(r => new RepositorySummary(
                    r.Id.Value,
                    r.Slug.ToString(),
                    credential.Id.Value,
                    credential.Name,
                    providerType,
                    RepositoryMappings.ToSeconds(r.PollInterval),
                    r.IsActive,
                    r.LastPolledAt,
                    RepositoryMappings.ToEligibilityInfo(r.Eligibility),
                    r.Position,
                    r.UntrackSuppressedSince))
                .ToList();

            return Result<IReadOnlyList<RepositorySummary>>.Ok(repositories);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet(string.Empty, static async (
                    Guid accountId,
                    IQueryHandler<Query, IReadOnlyList<RepositorySummary>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IReadOnlyList<RepositorySummary>> result = await handler.HandleAsync(
                        new Query(accountId),
                        cancellationToken);

                    return result.Match<Results<Ok<IReadOnlyList<RepositorySummary>>, BadRequest<string>>>(
                        repositories => TypedResults.Ok(repositories),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("GetRepositories")
                .WithSummary("Gets all monitored repositories for an account")
                .Produces<IReadOnlyList<RepositorySummary>>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
