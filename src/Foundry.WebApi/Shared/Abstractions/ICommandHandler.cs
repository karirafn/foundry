namespace Foundry.WebApi.Shared.Abstractions;

public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : notnull
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
