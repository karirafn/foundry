using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.IntegrationEventHandlerIdentityAttributeTests;

public sealed class Constructor
{
    [Fact]
    public void WhenValidIdentity_SetsIdentityProperty()
    {
        // Arrange
        const string Identity = "Foundry.Modules.Workers.WorkerStartedHandler";

        // Act
        IntegrationEventHandlerIdentityAttribute attribute = new(Identity);

        // Assert
        attribute.Identity.ShouldBe(Identity);
    }

    [Fact]
    public void WhenNullIdentity_ThrowsArgumentException()
    {
        // Arrange
        // Act / Assert
        Should.Throw<ArgumentException>(() => new IntegrationEventHandlerIdentityAttribute(null!));
    }

    [Fact]
    public void WhenEmptyIdentity_ThrowsArgumentException()
    {
        // Arrange
        // Act / Assert
        Should.Throw<ArgumentException>(() => new IntegrationEventHandlerIdentityAttribute(string.Empty));
    }

    [Fact]
    public void WhenWhitespaceIdentity_ThrowsArgumentException()
    {
        // Arrange
        // Act / Assert
        Should.Throw<ArgumentException>(() => new IntegrationEventHandlerIdentityAttribute("   "));
    }

    [Fact]
    public void WhenAppliedToClass_IsDiscoverableViaReflection()
    {
        // Arrange
        // Act
        IntegrationEventHandlerIdentityAttribute? attribute =
            typeof(TestHandlerWithIdentity).GetCustomAttributes(
                typeof(IntegrationEventHandlerIdentityAttribute),
                inherit: false)
            .Cast<IntegrationEventHandlerIdentityAttribute>()
            .SingleOrDefault();

        // Assert
        attribute.ShouldNotBeNull();
        attribute.Identity.ShouldBe("test-handler-stable-id");
    }

    [IntegrationEventHandlerIdentity("test-handler-stable-id")]
    private sealed class TestHandlerWithIdentity;
}
