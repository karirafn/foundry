using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.StateChanges;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssueStateRegistryTests;

public sealed class WhenName_IsUnknown
{
    [Theory]
    [InlineData("")]
    [InlineData("DETECTED")]
    [InlineData("active")]
    [InlineData("resolved")]
    [InlineData("unknown_state")]
    public void IsKnownName_ReturnsFalse(string name)
    {
        // Arrange
        // (name is the system under test input)

        // Act
        bool isKnown = IssueStateRegistry.IsKnownName(name);

        // Assert
        isKnown.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("DETECTED")]
    [InlineData("unknown_state")]
    public void GetEntityType_ReturnsNull(string name)
    {
        // Arrange
        // (name is the system under test input)

        // Act
        Type? entityType = IssueStateRegistry.GetEntityType(name);

        // Assert
        entityType.ShouldBeNull();
    }
}
