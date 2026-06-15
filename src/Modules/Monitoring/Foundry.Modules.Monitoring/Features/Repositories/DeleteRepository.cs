using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class DeleteRepository
{
    internal sealed record Command(Guid AccountId, Guid Id) : ICommand<bool>;

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, bool>
    {
        public async Task<Result<bool>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            AccountId accountId = AccountId.From(command.AccountId);
            MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(command.Id);

            if (await dbContext.Set<MonitoredRepository>()
                    .FirstOrDefaultAsync(
                        r => r.Id == repositoryId && r.AccountId == accountId,
                        cancellationToken)
                is not MonitoredRepository repository)
            {
                return Result<bool>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            dbContext.Set<MonitoredRepository>().Remove(repository);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<bool>.Ok(true);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapDelete("{id:guid}", static async (
                    Guid accountId,
                    Guid id,
                    ICommandHandler<Command, bool> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(accountId, id);
                    Result<bool> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<NoContent, NotFound>>(
                        _ => TypedResults.NoContent(),
                        _ => TypedResults.NotFound());
                })
                .WithName("DeleteRepository")
                .WithSummary("Deletes a monitored repository")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
