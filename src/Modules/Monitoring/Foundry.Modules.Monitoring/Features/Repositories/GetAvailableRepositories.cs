using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class GetAvailableRepositories
{
    internal sealed record Query(Guid AccountId) : IQuery<IReadOnlyList<ProviderRepository>>;

    internal sealed class Handler(
        DbContext dbContext,
        GitHubHttpClient gitHubHttpClient,
        GitLabHttpClient gitLabHttpClient)
        : IQueryHandler<Query, IReadOnlyList<ProviderRepository>>
    {
        public async Task<Result<IReadOnlyList<ProviderRepository>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            CredentialId credentialId = CredentialId.From(query.AccountId);

            Credential? credential = await dbContext.Set<Credential>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

            if (credential is null)
            {
                return Result<IReadOnlyList<ProviderRepository>>.Fail(
                    RepositoryErrors.AccountNotFound(credentialId));
            }

            if (credential.Token is null)
            {
                return Result<IReadOnlyList<ProviderRepository>>.Fail(
                    RepositoryErrors.AccountHasNoToken(credentialId));
            }

            return credential switch
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
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("available-repositories", static async (
                    Guid accountId,
                    IQueryHandler<Query, IReadOnlyList<ProviderRepository>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IReadOnlyList<ProviderRepository>> result = await handler.HandleAsync(
                        new Query(accountId),
                        cancellationToken);

                    return result.Match<Results<Ok<IReadOnlyList<ProviderRepository>>, NotFound<string>, BadRequest<string>>>(
                        repositories => TypedResults.Ok(repositories),
                        error => error.Code switch
                        {
                            RepositoryErrors.AccountNotFoundCode => TypedResults.NotFound(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("GetAvailableRepositories")
                .WithSummary("Gets all repositories available for a given account")
                .Produces<IReadOnlyList<ProviderRepository>>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
