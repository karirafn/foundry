using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// Lists the Unclaimed Repositories: membership is resolved live from the Namespace Claims, never from stored
/// eligibility, so a repository leaves the list as soon as an account claims its namespace.
/// </summary>
internal static class GetUnclaimedRepositories
{
    internal sealed record Query : IQuery<IReadOnlyList<UnclaimedRepositorySummary>>;

    internal sealed class Handler(DbContext dbContext, ICredentialResolver credentialResolver)
        : IQueryHandler<Query, IReadOnlyList<UnclaimedRepositorySummary>>
    {
        public async Task<Result<IReadOnlyList<UnclaimedRepositorySummary>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            List<MonitoredRepository> repositories = await dbContext.Set<MonitoredRepository>()
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            List<UnclaimedRepositorySummary> unclaimed = [];

            foreach (MonitoredRepository repository in repositories)
            {
                if (await credentialResolver.ResolveAsync(repository.Host, repository.Slug, cancellationToken) is not null)
                {
                    continue;
                }

                string namespaceName = Namespace.PrefixesOf(repository.Slug)[^1].Value;
                unclaimed.Add(new UnclaimedRepositorySummary(
                    repository.Id.Value,
                    repository.Host,
                    repository.Slug.ToString(),
                    EligibilityViolationInfo.NoCredentialRule(namespaceName),
                    EligibilityViolationInfo.NoCredentialDescription(namespaceName)));
            }

            return Result<IReadOnlyList<UnclaimedRepositorySummary>>.Ok(
            [
                .. unclaimed
                    .OrderBy(r => r.Host, StringComparer.Ordinal)
                    .ThenBy(r => r.Slug, StringComparer.Ordinal),
            ]);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("unclaimed", static async (
                    IQueryHandler<Query, IReadOnlyList<UnclaimedRepositorySummary>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IReadOnlyList<UnclaimedRepositorySummary>> result =
                        await handler.HandleAsync(new Query(), cancellationToken);

                    return result.Match<Results<Ok<IReadOnlyList<UnclaimedRepositorySummary>>, ProblemHttpResult>>(
                        repositories => TypedResults.Ok(repositories),
                        error => error.ToProblem(StatusCodes.Status400BadRequest));
                })
                .WithName("GetUnclaimedRepositories")
                .WithSummary("Gets the monitored repositories no account's namespace claim covers")
                .Produces<IReadOnlyList<UnclaimedRepositorySummary>>();
        }
    }
}
