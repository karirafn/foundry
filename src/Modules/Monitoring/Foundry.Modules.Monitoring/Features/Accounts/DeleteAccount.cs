using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class DeleteAccount
{
    internal sealed record Command(CredentialId Id) : ICommand<bool>;

    internal sealed class Handler(
        DbContext dbContext,
        RepositoryRemoval removal,
        TimeProvider timeProvider,
        IDomainEventDispatcher domainEventDispatcher) : ICommandHandler<Command, bool>
    {
        public async Task<Result<bool>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (await dbContext.Set<Credential>()
                    .Include(c => c.Namespaces)
                    .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
                is not Credential credential)
            {
                return Result<bool>.Fail(CredentialErrors.NotFound(command.Id));
            }

            List<MonitoredRepository> covered = await LoadCoveredRepositoriesAsync(credential, cancellationToken);

            // Removed while the account still exists, so each repository's hooks are deleted with its token.
            if (covered.Count > 0
                && await removal.RemoveAsync(covered, timeProvider.GetUtcNow(), cancellationToken) is Result<bool>.Failure failure)
            {
                return failure;
            }

            credential.Delete();
            dbContext.Set<Credential>().Remove(credential);
            await dbContext.SaveChangesAsync(cancellationToken);

            // The deletion is committed, so a handler that recounts accounts no longer sees this one.
            IDomainEvent[] events = [.. credential.DomainEvents];
            credential.ClearDomainEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);

            return Result<bool>.Ok(true);
        }

        // The same coverage rule as the account's repository listing.
        private async Task<List<MonitoredRepository>> LoadCoveredRepositoriesAsync(
            Credential credential,
            CancellationToken cancellationToken)
        {
            List<MonitoredRepository> hostRepositories = await dbContext.Set<MonitoredRepository>()
                .Where(r => r.Host == credential.Host)
                .OrderBy(r => r.Position)
                .ToListAsync(cancellationToken);

            return [.. hostRepositories.Where(r => credential.ResolveCoveringNamespace(r.Slug) is not null)];
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapDelete("{id:guid}", static async (
                    Guid id,
                    ICommandHandler<Command, bool> handler,
                    CancellationToken cancellationToken) =>
                {
                    CredentialId credentialId = CredentialId.From(id);
                    Command command = new(credentialId);
                    Result<bool> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<NoContent, ProblemHttpResult>>(
                        _ => TypedResults.NoContent(),
                        error => error switch
                        {
                            RemovalBlockedError blocked => blocked.ToConflictProblem(),
                            _ => error.ToProblem(StatusCodes.Status404NotFound),
                        });
                })
                .WithName("DeleteAccount")
                .WithSummary("Deletes an account and removes every repository it covers")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
