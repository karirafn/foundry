using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.HandlerDedupIdentityRegistryTests;

public sealed class Register
{
    [Fact]
    public void WhenAnnotatedHandler_IdentityForReturnsTheDeclaredIdentity()
    {
        // Arrange
        HandlerDedupIdentityRegistry sut = new();

        // Act
        sut.Register(typeof(AnnotatedHandlerAlpha));

        // Assert
        sut.IdentityFor(typeof(AnnotatedHandlerAlpha)).ShouldBe("handler-alpha");
    }

    [Fact]
    public void WhenHandlerLacksAttribute_ThrowsNamingTheType()
    {
        // Arrange
        HandlerDedupIdentityRegistry sut = new();

        // Act
        InvalidOperationException ex = Should.Throw<InvalidOperationException>(
            () => sut.Register(typeof(UnannotatedHandler)));

        // Assert
        ex.Message.ShouldContain(typeof(UnannotatedHandler).FullName!);
    }

    [Fact]
    public void WhenSameTypeRegisteredTwice_IsIdempotentAndDoesNotThrow()
    {
        // Arrange
        HandlerDedupIdentityRegistry sut = new();
        sut.Register(typeof(AnnotatedHandlerAlpha));

        // Act
        sut.Register(typeof(AnnotatedHandlerAlpha));

        // Assert
        sut.IdentityFor(typeof(AnnotatedHandlerAlpha)).ShouldBe("handler-alpha");
    }

    [Fact]
    public void WhenIdentityForCalledOnUnregisteredType_Throws()
    {
        // Arrange
        HandlerDedupIdentityRegistry sut = new();

        // Act
        InvalidOperationException ex = Should.Throw<InvalidOperationException>(
            () => sut.IdentityFor(typeof(AnnotatedHandlerAlpha)));

        // Assert
        ex.Message.ShouldContain(typeof(AnnotatedHandlerAlpha).FullName!);
    }
}
