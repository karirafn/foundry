using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
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

    internal sealed class Handler(DbContext dbContext, IDomainEventDispatcher domainEventDispatcher) : ICommandHandler<Command, bool>
    {
        public async Task<Result<bool>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (await dbContext.Set<Credential>()
                    .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
                is not Credential credential)
            {
                return Result<bool>.Fail(CredentialErrors.NotFound(command.Id));
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
                        error => error.ToProblem(StatusCodes.Status404NotFound));
                })
                .WithName("DeleteAccount")
                .WithSummary("Deletes an account")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
