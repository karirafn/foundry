using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class GetAccounts
{
    internal sealed record Query : IQuery<IReadOnlyList<CredentialSummary>>;

    internal sealed class Handler(DbContext dbContext, ILogger<Handler> logger)
        : IQueryHandler<Query, IReadOnlyList<CredentialSummary>>
    {
        public async Task<Result<IReadOnlyList<CredentialSummary>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            // Materialize Credential entities so ProviderTokenConverter runs and the Unreadable
            // state becomes observable. Accounts are few (one per PAT), so per-row decryption
            // on list is acceptable; a SQL projection cannot distinguish Present from Unreadable.
            List<Credential> credentials = await dbContext.Set<Credential>()
                .Include(a => a.Namespaces)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (Credential credential in credentials)
            {
                credential.WarnIfTokenUnreadable(logger);
            }

            List<CredentialSummary> summaries = credentials
                .Select(a => new CredentialSummary(
                    a.Id.Value,
                    a.Name,
                    a is GitLabCredential ? ProviderTypes.GitLab : ProviderTypes.GitHub,
                    a.BaseUrl.Value.ToString(),
                    a.Token is not null,
                    AccountsDatabaseHelpers.ToTokenStatus(a.Token),
                    a.Namespaces.Select(n => n.Value).ToList()))
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

                    return result.Match<Results<Ok<IReadOnlyList<CredentialSummary>>, ProblemHttpResult>>(
                        credentials => TypedResults.Ok(credentials),
                        error => error.ToProblem(StatusCodes.Status400BadRequest));
                })
                .WithName("GetAccounts")
                .WithSummary("Gets all configured accounts")
                .Produces<IReadOnlyList<CredentialSummary>>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
