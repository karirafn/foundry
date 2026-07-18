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

internal static class DeleteRepository
{
    internal sealed record Command(Guid AccountId, Guid Id) : ICommand<bool>;

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, bool>
    {
        public async Task<Result<bool>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            CredentialId credentialId = CredentialId.From(command.AccountId);
            MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(command.Id);

            if (await dbContext.Set<MonitoredRepository>()
                    .Where(r => r.Id == repositoryId)
                    .FirstOrDefaultAsync(r => r.CredentialId == credentialId, cancellationToken)
                is not MonitoredRepository repository)
            {
                return Result<bool>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            await using IDbContextTransaction tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            dbContext.Set<MonitoredRepository>().Remove(repository);
            await dbContext.SaveChangesAsync(cancellationToken);

            List<MonitoredRepository> survivors = await dbContext
                .Set<MonitoredRepository>()
                .OrderBy(r => r.Position)
                .ToListAsync(cancellationToken);

            await RepositoryRenumber.RenumberAsync(dbContext, survivors, cancellationToken);

            await tx.CommitAsync(cancellationToken);

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
