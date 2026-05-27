namespace Foundry.WebApi.Shared.Abstractions;

public interface ICommandValidator<TCommand>
{
    Result Validate(TCommand command);
}
