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
            // Project directly to avoid materializing Credential entities, which would decrypt
            // the encrypted Token column via the value converter even when only HasToken is needed.
            List<CredentialSummary> summaries = await dbContext.Set<Credential>()
                .AsNoTracking()
                .Select(a => new CredentialSummary(
                    a.Id.Value,
                    a.Name,
                    a is GitLabCredential ? ProviderTypes.GitLab : ProviderTypes.GitHub,
                    a.BaseUrl.Value.ToString(),
                    a.Token != null,
                    a.Namespaces.Select(n => n.Value).ToList()))
                .ToListAsync(cancellationToken);

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
