namespace Foundry.WebApi.Shared.Abstractions;

public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : notnull
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
