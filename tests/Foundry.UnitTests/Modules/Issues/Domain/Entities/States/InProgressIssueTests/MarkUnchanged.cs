using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.InProgressIssueTests;

public sealed class MarkUnchanged
{
    [Fact]
    public void WhenMarkedUnchanged_ReturnsUnchangedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        UnchangedIssue unchanged = inProgress.MarkUnchanged();

        // Assert
        unchanged.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenMarkedUnchanged_RaisesIssueUnchangedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        inProgress.MarkUnchanged();

        // Assert
        IssueUnchanged domainEvent = inProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueUnchanged>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedUnchanged_UnchangedIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        UnchangedIssue unchanged = inProgress.MarkUnchanged();

        // Assert
        unchanged.ShouldSatisfyAllConditions(
            () => unchanged.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => unchanged.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => unchanged.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => unchanged.Title.ShouldBe(inProgress.Title),
            () => unchanged.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }
}
