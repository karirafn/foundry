using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class GetAccounts
{
    internal sealed record Query : IQuery<IReadOnlyList<CredentialSummary>>;

    internal sealed class Handler(DbContext dbContext)
        : IQueryHandler<Query, IReadOnlyList<CredentialSummary>>
    {
        public async Task<Result<IReadOnlyList<CredentialSummary>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            List<Credential> credentials = await dbContext.Set<Credential>()
                .AsNoTracking()
                .Include(a => a.Namespaces)
                .ToListAsync(cancellationToken);

            List<CredentialSummary> summaries = credentials
                .Select(r => new CredentialSummary(
                    r.Id.Value,
                    r.Name,
                    r is GitLabCredential ? ProviderTypes.GitLab : ProviderTypes.GitHub,
                    r.BaseUrl.Value.ToString(),
                    r.Token is not null,
                    r.Namespaces.Select(n => n.Value).ToList()))
                .ToList();

            return Result<IReadOnlyList<CredentialSummary>>.Ok(summaries);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet(string.Empty, static async (
                    IQueryHandler<Query, IReadOnlyList<CredentialSummary>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IReadOnlyList<CredentialSummary>> result = await handler.HandleAsync(
                        new Query(),
                        cancellationToken);

                    return result.Match<Results<Ok<IReadOnlyList<CredentialSummary>>, BadRequest<string>>>(
                        credentials => TypedResults.Ok(credentials),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("GetAccounts")
                .WithSummary("Gets all configured accounts")
                .Produces<IReadOnlyList<CredentialSummary>>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
