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
        Action validate = () => sut.Validate();

        // Assert
        Should.NotThrow(validate);
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

    [Fact]
    public void WhenTwoIndependentCollisionsExist_ThrowsReportingBothCollisions()
    {
        // Arrange
        HandlerDedupIdentityRegistry sut = new();
        sut.Register(typeof(AnnotatedHandlerBeta));
        sut.Register(typeof(CollidingHandlerBeta));
        sut.Register(typeof(AnnotatedHandlerGamma));
        sut.Register(typeof(CollidingHandlerGamma));

        // Act
        InvalidOperationException ex = Should.Throw<InvalidOperationException>(() => sut.Validate());

        // Assert
        ex.Message.ShouldSatisfyAllConditions(
            () => ex.Message.ShouldContain("handler-beta"),
            () => ex.Message.ShouldContain("handler-gamma"),
            () => ex.Message.ShouldContain(typeof(AnnotatedHandlerBeta).FullName!),
            () => ex.Message.ShouldContain(typeof(CollidingHandlerBeta).FullName!),
            () => ex.Message.ShouldContain(typeof(AnnotatedHandlerGamma).FullName!),
            () => ex.Message.ShouldContain(typeof(CollidingHandlerGamma).FullName!));
    }
}
