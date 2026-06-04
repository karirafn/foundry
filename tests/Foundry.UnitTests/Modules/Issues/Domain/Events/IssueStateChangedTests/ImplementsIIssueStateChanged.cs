using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Events.IssueStateChangedTests;

public sealed class ImplementsIIssueStateChanged
{
    private static IssueId AnyIssueId => IssueId.New();

    private static MonitoredRepositoryId AnyRepositoryId => MonitoredRepositoryId.New();

    [Fact]
    public void IssueQueued_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueQueued @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueBlocked_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueBlocked @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueCompleted_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueCompleted @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueFailed_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueFailed @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueInReview_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueInReview @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueUnchanged_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueUnchanged @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueDismissed_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueDismissed @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueRevisionQueued_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueRevisionQueued @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueRevisionFailed_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueRevisionFailed @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void IssueIneligible_ImplementsIIssueStateChanged()
    {
        // Arrange
        IssueIneligible @event = new(AnyIssueId, AnyRepositoryId);

        // Act & Assert
        @event.ShouldBeAssignableTo<IIssueStateChanged>();
    }

    [Fact]
    public void CircularDependencyDetected_DoesNotImplementIIssueStateChanged()
    {
        // Arrange
        CircularDependencyDetected @event = new(AnyRepositoryId, []);

        // Act & Assert
        @event.ShouldNotBeAssignableTo<IIssueStateChanged>();
    }
}
