using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ServiceCollectionExtensionsTests;

public sealed class AddCommandHandlerWithValidator
{
    [Fact]
    public void WhenRegistered_ResolvesValidatingCommandHandlerWrappingInnerHandler()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddCommandHandler<DiTestCommand, string, DiTestCommandHandler, DiTestCommandValidator>();
        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        ICommandHandler<DiTestCommand, string> handler = provider.GetRequiredService<ICommandHandler<DiTestCommand, string>>();

        // Assert
        handler.ShouldBeOfType<ValidatingCommandHandler<DiTestCommand, string>>();
    }

    [Fact]
    public void WhenRegistered_InnerHandlerIsAvailableAsConcrete()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddCommandHandler<DiTestCommand, string, DiTestCommandHandler, DiTestCommandValidator>();
        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        DiTestCommandHandler innerHandler = provider.GetRequiredService<DiTestCommandHandler>();

        // Assert
        innerHandler.ShouldNotBeNull();
    }

    [Fact]
    public void WhenRegistered_ValidatorIsAvailableViaInterface()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddCommandHandler<DiTestCommand, string, DiTestCommandHandler, DiTestCommandValidator>();
        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        ICommandValidator<DiTestCommand> validator = provider.GetRequiredService<ICommandValidator<DiTestCommand>>();

        // Assert
        validator.ShouldBeOfType<DiTestCommandValidator>();
    }
}
