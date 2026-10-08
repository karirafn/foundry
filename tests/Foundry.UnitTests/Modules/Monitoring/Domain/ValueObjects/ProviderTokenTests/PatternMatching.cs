using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.ProviderTokenTests;

public sealed class PatternMatching
{
    [Fact]
    public void WhenPresent_IsPatternMatchesPresent()
    {
        // Arrange
        ProviderToken token = new ProviderToken.Present("tok-abc");

        // Act
        bool result = token is ProviderToken.Present;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenUnreadable_IsPatternMatchesUnreadable()
    {
        // Arrange
        ProviderToken token = new ProviderToken.Unreadable();

        // Act
        bool result = token is ProviderToken.Unreadable;

        // Assert
        result.ShouldBeTrue();
    }
}
