using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ValidatingCommandHandlerTests;

internal sealed record TestCommand(string Payload) : ICommand<string>;

internal sealed class TestCommandHandler : ICommandHandler<TestCommand, string>
{
    public bool WasCalled { get; private set; }

    public Task<Result<string>> HandleAsync(TestCommand command, CancellationToken cancellationToken)
    {
        WasCalled = true;
        return Task.FromResult(Result<string>.Ok(command.Payload));
    }
}

internal sealed class TestCommandValidator(bool valid) : ICommandValidator<TestCommand>
{
    private static readonly Error ValidationError = new("test.invalid", "Test validation failed");

    public Result Validate(TestCommand command)
    {
        return valid ? Result.Ok() : Result.Fail(ValidationError);
    }
}
