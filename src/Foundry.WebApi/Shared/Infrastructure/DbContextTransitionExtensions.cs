using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.WebApi.Shared.Infrastructure;

public static class DbContextTransitionExtensions
{
    public static async Task TransitionAsync<TFrom, TTo>(
        this DbContext db,
        TFrom old,
        TTo next,
        CancellationToken cancellationToken = default)
        where TFrom : class, IStateMachine
        where TTo : class, IStateMachine
    {
        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        db.Remove(old);
        await db.SaveChangesAsync(cancellationToken);

        db.Add(next);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
