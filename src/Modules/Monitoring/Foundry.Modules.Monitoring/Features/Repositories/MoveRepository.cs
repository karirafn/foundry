using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class MoveRepository
{
    internal sealed record Command(Guid Id, int Position) : ICommand<bool>;

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, bool>
    {
        public async Task<Result<bool>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(command.Id);

            List<MonitoredRepository> allRepositories = await dbContext
                .Set<MonitoredRepository>()
                .OrderBy(r => r.Position)
                .ToListAsync(cancellationToken);

            MonitoredRepository? target = allRepositories.FirstOrDefault(r => r.Id == repositoryId);

            if (target is null)
            {
                return Result<bool>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            int count = allRepositories.Count;
            int clampedPosition = Math.Clamp(command.Position, 0, count - 1);

            int currentPosition = allRepositories.IndexOf(target);
            if (currentPosition == clampedPosition)
            {
                return Result<bool>.Ok(true);
            }

            allRepositories.RemoveAt(currentPosition);
            allRepositories.Insert(clampedPosition, target);

            await using IDbContextTransaction tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await RepositoryRenumber.RenumberAsync(dbContext, allRepositories, cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Result<bool>.Ok(true);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(int Position);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapMethods("{id:guid}/position", ["PATCH"], static async (
                    Guid id,
                    RequestBody body,
                    ICommandHandler<Command, bool> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(id, body.Position);
                    Result<bool> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<NoContent, NotFound>>(
                        _ => TypedResults.NoContent(),
                        _ => TypedResults.NotFound());
                })
                .WithName("MoveRepository")
                .WithSummary("Moves a monitored repository to the specified position in the dispatch order")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
