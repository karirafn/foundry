using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.PullRequestChangesRequestedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewComment comment = new("Please fix the null check");

        // Act
        PullRequestChangesRequested @event = new(
            repositoryId,
            IssueNumber: 42,
            Comments: [comment]);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.ShouldSatisfyAllConditions(
            () => @event.RepositoryId.ShouldBe(repositoryId),
            () => @event.IssueNumber.ShouldBe(42),
            () => @event.Comments.Count.ShouldBe(1),
            () => @event.Comments[0].ShouldBe(comment));
    }

    [Fact]
    public void WhenCreatedWithoutOptionalFields_DefaultsAreApplied()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        PullRequestChangesRequested @event = new(
            repositoryId,
            IssueNumber: 1,
            Comments: []);

        // Assert
        @event.ShouldSatisfyAllConditions(
            () => @event.OmittedCommentCount.ShouldBe(0),
            () => @event.NewestCommentAt.ShouldBeNull());
    }

    [Fact]
    public void WhenCreatedWithOmittedCountAndNewestCommentAt_CarriesThoseValues()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset newestCommentAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        PullRequestChangesRequested @event = new(
            repositoryId,
            IssueNumber: 7,
            Comments: [new ReviewComment("Fix this")],
            OmittedCommentCount: 5,
            NewestCommentAt: newestCommentAt);

        // Assert
        @event.ShouldSatisfyAllConditions(
            () => @event.OmittedCommentCount.ShouldBe(5),
            () => @event.NewestCommentAt.ShouldBe(newestCommentAt));
    }
}
