using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.ActiveRunTests;

public sealed class Complete
{
    private static ActiveRun CreateActiveRun(IssueId? issueId = null)
    {
        StartingRun starting = StartingRun.Begin(issueId ?? IssueId.New(), WorkerRunId.New());
        return starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/1-default"), MonitoredRepositoryId.New());
    }

    [Fact]
    public void WhenCalled_ReturnsCompletedRunWithSameId()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        CompletedRun completed = active.Complete(0, null, null);

        // Assert
        completed.Id.ShouldBe(active.Id);
    }

    [Fact]
    public void WhenCalled_CopiesSharedProperties()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun active = CreateActiveRun(issueId);

        // Act
        CompletedRun completed = active.Complete(0, null, null);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.IssueId.ShouldBe(issueId),
            () => completed.CreatedAt.ShouldBe(active.CreatedAt));
    }

    [Fact]
    public void WhenCalled_SetsExitCode()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        CompletedRun completed = active.Complete(42, null, null);

        // Assert
        completed.ExitCode.ShouldBe(42);
    }

    [Fact]
    public void WhenCalled_SetsCompletedAtToUtcNow()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        CompletedRun completed = active.Complete(0, null, null);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        completed.CompletedAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenBranchNameProvided_SetsBranchName()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        CompletedRun completed = active.Complete(0, BranchName.From("feat/issue-5"), null);

        // Assert
        completed.BranchName.ShouldBe(BranchName.From("feat/issue-5"));
    }

    [Fact]
    public void WhenPullRequestUrlProvided_SetsPullRequestUrl()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        CompletedRun completed = active.Complete(0, null, PullRequestUrl.From("https://github.com/owner/repo/pull/10"));

        // Assert
        completed.PullRequestUrl.ShouldBe(PullRequestUrl.From("https://github.com/owner/repo/pull/10"));
    }

    [Fact]
    public void WhenOptionalFieldsOmitted_FieldsAreNull()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        CompletedRun completed = active.Complete(0, null, null);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBeNull(),
            () => completed.PullRequestUrl.ShouldBeNull());
    }

    [Fact]
    public void WhenCalled_RaisesWorkerRunCompletedOnActiveRun()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun active = CreateActiveRun(issueId);

        // Act
        active.Complete(0, BranchName.From("feat/issue-5"), PullRequestUrl.From("https://github.com/owner/repo/pull/10"));

        // Assert
        WorkerRunCompleted domainEvent = active.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunCompleted>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(active.Id),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.BranchName.ShouldBe("feat/issue-5"),
            () => domainEvent.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"));
    }
}
