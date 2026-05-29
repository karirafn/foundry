using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Abstractions.IIntegrationEventHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenEventTypeMismatches_ReturnsFaultedTask()
    {
        // Arrange
        TypedHandler sut = new();
        IIntegrationEventHandler nonGenericHandler = sut;
        WrongEvent wrongEvent = new("wrong");

        // Act
        Task act = nonGenericHandler.HandleAsync(wrongEvent, CancellationToken.None);

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WhenEventTypeMismatches_ErrorMessageNamesExpectedAndActualTypes()
    {
        // Arrange
        TypedHandler sut = new();
        IIntegrationEventHandler nonGenericHandler = sut;
        WrongEvent wrongEvent = new("wrong");

        // Act
        Task act = nonGenericHandler.HandleAsync(wrongEvent, CancellationToken.None);
        InvalidOperationException ex = await act.ShouldThrowAsync<InvalidOperationException>();

        // Assert
        ex.Message.ShouldSatisfyAllConditions(
            () => ex.Message.ShouldContain(nameof(CorrectEvent)),
            () => ex.Message.ShouldContain(nameof(WrongEvent))
        );
    }
}

internal sealed record CorrectEvent(string Name) : IIntegrationEvent;

internal sealed record WrongEvent(string Name) : IIntegrationEvent;

internal sealed class TypedHandler : IIntegrationEventHandler<CorrectEvent>
{
    public Task HandleAsync(CorrectEvent @event, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
