using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.HandlerDedupIdentityRegistryTests;

public sealed class Validate
{
    [Fact]
    public void WhenAllIdentitiesAreDistinct_DoesNotThrow()
    {
        // Arrange
        HandlerDedupIdentityRegistry sut = new();
        sut.Register(typeof(AnnotatedHandlerAlpha));
        sut.Register(typeof(AnnotatedHandlerBeta));

        // Act
        // Assert
        Should.NotThrow(() => sut.Validate());
    }

    [Fact]
    public void WhenTwoHandlersDeclareTheSameIdentity_ThrowsNamingBothTypesAndTheCollidingIdentity()
    {
        // Arrange
        HandlerDedupIdentityRegistry sut = new();
        sut.Register(typeof(AnnotatedHandlerAlpha));
        sut.Register(typeof(CollidingHandler));

        // Act
        InvalidOperationException ex = Should.Throw<InvalidOperationException>(() => sut.Validate());

        // Assert
        ex.Message.ShouldSatisfyAllConditions(
            () => ex.Message.ShouldContain(typeof(AnnotatedHandlerAlpha).FullName!),
            () => ex.Message.ShouldContain(typeof(CollidingHandler).FullName!),
            () => ex.Message.ShouldContain("handler-alpha"));
    }
}
