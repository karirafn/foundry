using Foundry.Modules.Issues.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssueStateRegistryTests;

public sealed class WhenName_IsKnown
{
    [Theory]
    [InlineData("detected")]
    [InlineData("queued")]
    [InlineData("blocked")]
    [InlineData("in_progress")]
    [InlineData("review")]
    [InlineData("failed")]
    [InlineData("continuable_failed")]
    [InlineData("continuation_queued")]
    [InlineData("revision_queued")]
    [InlineData("revision_in_progress")]
    [InlineData("revision_failed")]
    [InlineData("completed")]
    [InlineData("unchanged")]
    public void AllNames_AreRecognisedAsValid(string name)
    {
        // Arrange
        // (name is the system under test input)

        // Act
        bool isKnown = IssueStateRegistry.IsKnownName(name);

        // Assert
        isKnown.ShouldBeTrue();
    }
}
