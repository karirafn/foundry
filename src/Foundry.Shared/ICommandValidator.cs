namespace Foundry.Shared;

public interface ICommandValidator<TCommand>
{
    Result Validate(TCommand command);
}
