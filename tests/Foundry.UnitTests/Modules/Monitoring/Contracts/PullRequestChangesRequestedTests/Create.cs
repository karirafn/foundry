using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;

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

    [Fact]
    public void WhenLegacyJsonOmitsOmittedCommentCountAndNewestCommentAt_DeserializesWithDefaults()
    {
        // Arrange — simulate a pre-feature outbox_messages row serialized BEFORE OmittedCommentCount
        // and NewestCommentAt were added. STJ must fill those optional parameters with their defaults
        // (0 and null) rather than throwing, proving in-flight rows survive the schema evolution.
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(Guid.NewGuid());
        string legacyJson = $$"""
            {
                "RepositoryId": {"Value": "{{repositoryId.Value}}"},
                "IssueNumber": 42,
                "Comments": [{"Body": "Fix the null check", "FilePath": null, "Line": null}]
            }
            """;

        // Act
        PullRequestChangesRequested? deserialized = JsonSerializer.Deserialize<PullRequestChangesRequested>(
            legacyJson,
            OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.RepositoryId.ShouldBe(repositoryId),
            () => deserialized.IssueNumber.ShouldBe(42),
            () => deserialized.OmittedCommentCount.ShouldBe(0),
            () => deserialized.NewestCommentAt.ShouldBeNull());
    }
}
