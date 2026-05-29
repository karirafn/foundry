using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Abstractions.ServiceCollectionExtensionsTests;

internal sealed record DiTestCommand(string Payload) : ICommand<string>;

internal sealed class DiTestCommandHandler : ICommandHandler<DiTestCommand, string>
{
    public Task<Result<string>> HandleAsync(DiTestCommand command, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<string>.Ok(command.Payload));
    }
}

internal sealed class DiTestCommandValidator : ICommandValidator<DiTestCommand>
{
    public Result Validate(DiTestCommand command)
    {
        return Result.Ok();
    }
}
