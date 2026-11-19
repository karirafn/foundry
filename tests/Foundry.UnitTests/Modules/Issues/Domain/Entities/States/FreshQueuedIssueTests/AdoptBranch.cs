using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.FreshQueuedIssueTests;

public sealed class AdoptBranch
{
    [Fact]
    public void WhenAdopted_QueuesContinuationOnTheGeneratedBranchWithoutFailureReason()
    {
        // Arrange
        FreshQueuedIssue queued = new IssueBuilder().WithIssueNumber(42).FreshQueued();
        string branchName = queued.GeneratedBranchName.Value;
        DateTimeOffset adoptedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuationQueuedIssue continuation = queued.AdoptBranch(adoptedAt);

        // Assert
        continuation.ShouldSatisfyAllConditions(
            () => continuation.Id.ShouldBe(queued.Id),
            () => continuation.IssueNumber.ShouldBe(42),
            () => continuation.BranchName.ShouldBe(branchName),
            () => continuation.EnteredStateAt.ShouldBe(adoptedAt),
            () => continuation.Context.ShouldBe(new DispatchContext.Continuation(branchName, FailureReason: null)),
            () => queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationQueued>());
    }
}
