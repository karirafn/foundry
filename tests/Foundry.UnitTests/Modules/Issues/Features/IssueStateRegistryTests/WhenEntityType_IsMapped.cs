using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.StateChanges;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssueStateRegistryTests;

public sealed class WhenEntityType_IsMapped
{
    [Theory]
    [InlineData("detected", typeof(DetectedIssue))]
    [InlineData("queued", typeof(QueuedIssue))]
    [InlineData("blocked", typeof(BlockedIssue))]
    [InlineData("in_progress", typeof(InProgressIssue))]
    [InlineData("review", typeof(ReviewIssue))]
    [InlineData("failed", typeof(FailedIssue))]
    [InlineData("continuable_failed", typeof(ContinuableFailedIssue))]
    [InlineData("continuation_queued", typeof(ContinuationQueuedIssue))]
    [InlineData("revision_queued", typeof(RevisionQueuedIssue))]
    [InlineData("revision_in_progress", typeof(RevisionInProgressIssue))]
    [InlineData("revision_failed", typeof(RevisionFailedIssue))]
    [InlineData("completed", typeof(CompletedIssue))]
    [InlineData("unchanged", typeof(UnchangedIssue))]
    public void GetEntityType_ReturnsCorrectType(string name, Type expectedType)
    {
        // Arrange
        // (name and expectedType are the system under test inputs)

        // Act
        Type? entityType = IssueStateRegistry.GetEntityType(name);

        // Assert
        entityType.ShouldBe(expectedType);
    }
}
