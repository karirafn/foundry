using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Abstractions.ValidatingCommandHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenCommandIsValid_DelegatesToInnerHandler_ReturnsSuccess()
    {
        // Arrange
        TestCommandHandler inner = new();
        TestCommandValidator validator = new(valid: true);
        ValidatingCommandHandler<TestCommand, string> sut = new(inner, validator);
        TestCommand command = new("hello");

        // Act
        Result<string> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => result.ShouldBeOfType<Result<string>.Success>().Value.ShouldBe("hello"),
            () => inner.WasCalled.ShouldBeTrue()
        );
    }

    [Fact]
    public async Task WhenCommandIsInvalid_ReturnsValidationError_InnerHandlerNotCalled()
    {
        // Arrange
        TestCommandHandler inner = new();
        TestCommandValidator validator = new(valid: false);
        ValidatingCommandHandler<TestCommand, string> sut = new(inner, validator);
        TestCommand command = new("hello");

        // Act
        Result<string> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsFailure.ShouldBeTrue(),
            () => inner.WasCalled.ShouldBeFalse()
        );
    }
}
