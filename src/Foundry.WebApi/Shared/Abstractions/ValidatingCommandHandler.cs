namespace Foundry.WebApi.Shared.Abstractions;

internal sealed class ValidatingCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ICommandValidator<TCommand> validator)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : notnull
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        Result validation = validator.Validate(command);
        if (validation is Result.Failure f)
        {
            return Result<TResult>.Fail(f.Error);
        }

        return await inner.HandleAsync(command, cancellationToken);
    }
}
