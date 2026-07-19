using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class DeleteAccount
{
    internal sealed record Command(CredentialId Id) : ICommand<bool>;

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, bool>
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

            dbContext.Set<Credential>().Remove(credential);
            await dbContext.SaveChangesAsync(cancellationToken);

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

                    return result.Match<Results<NoContent, NotFound>>(
                        _ => TypedResults.NoContent(),
                        _ => TypedResults.NotFound());
                })
                .WithName("DeleteAccount")
                .WithSummary("Deletes an account")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
