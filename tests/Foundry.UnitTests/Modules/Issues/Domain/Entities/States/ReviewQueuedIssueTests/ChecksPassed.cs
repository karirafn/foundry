using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewQueuedIssueTests;

public sealed class ChecksPassed
{
    [Fact]
    public void WhenTheChecksGatePassed_TheContextSaysChecksPassed()
    {
        // Arrange
        ReviewQueuedIssue queued = new IssueBuilder().WithChecksOutcome(new ChecksOutcome.Passed()).ReviewQueued();

        // Act
        DispatchContext context = queued.Context;

        // Assert
        context.ShouldBeOfType<DispatchContext.Review>().ChecksPassed.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheChecksGateDetectedNoChecks_TheContextSaysChecksDidNotPass()
    {
        // Arrange
        ReviewQueuedIssue queued = new IssueBuilder().WithChecksOutcome(new ChecksOutcome.NoneDetected()).ReviewQueued();

        // Act
        DispatchContext context = queued.Context;

        // Assert
        context.ShouldBeOfType<DispatchContext.Review>().ChecksPassed.ShouldBeFalse();
    }
}
