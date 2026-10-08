using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.ProviderTokenTests;

public sealed class Equals
{
    [Fact]
    public void WhenTwoPresentTokensHaveSameValue_AreEqual()
    {
        // Arrange
        ProviderToken a = new ProviderToken.Present("tok-abc");
        ProviderToken b = new ProviderToken.Present("tok-abc");

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenPresentTokenAndUnreadable_AreNotEqual()
    {
        // Arrange
        ProviderToken present = new ProviderToken.Present("tok-abc");
        ProviderToken unreadable = new ProviderToken.Unreadable();

        // Act
        bool result = present.Equals(unreadable);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenTwoUnreadableTokens_AreEqual()
    {
        // Arrange
        ProviderToken a = new ProviderToken.Unreadable();
        ProviderToken b = new ProviderToken.Unreadable();

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }
}
