using Foundry.Modules.Issues.Features.StateChanges;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.StateChanges.IssueStateRegistryTests;

public sealed class WhenPartition_IsInspected
{
    [Fact]
    public void Active_ContainsExactly12Names()
    {
        // Arrange
        // (registry is the system under test)

        // Act
        IReadOnlySet<string> active = IssueStateRegistry.Active;

        // Assert
        active.Count.ShouldBe(12);
    }

    [Fact]
    public void Resolved_ContainsExactly1Name()
    {
        // Arrange
        // (registry is the system under test)

        // Act
        IReadOnlySet<string> resolved = IssueStateRegistry.Resolved;

        // Assert
        resolved.Count.ShouldBe(1);
    }

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
    [InlineData("unchanged")]
    public void Active_ContainsExpectedName(string name)
    {
        // Arrange
        // (name is the expected active state)

        // Act
        IReadOnlySet<string> active = IssueStateRegistry.Active;

        // Assert
        active.ShouldContain(name);
    }

    [Theory]
    [InlineData("completed")]
    public void Resolved_ContainsExpectedName(string name)
    {
        // Arrange
        // (name is the expected resolved state)

        // Act
        IReadOnlySet<string> resolved = IssueStateRegistry.Resolved;

        // Assert
        resolved.ShouldContain(name);
    }

    [Theory]
    [InlineData("completed")]
    public void Active_DoesNotContainResolvedName(string name)
    {
        // Arrange
        // (name is a resolved state)

        // Act
        IReadOnlySet<string> active = IssueStateRegistry.Active;

        // Assert
        active.ShouldNotContain(name);
    }

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
    [InlineData("unchanged")]
    public void Resolved_DoesNotContainActiveName(string name)
    {
        // Arrange
        // (name is an active state)

        // Act
        IReadOnlySet<string> resolved = IssueStateRegistry.Resolved;

        // Assert
        resolved.ShouldNotContain(name);
    }
}
