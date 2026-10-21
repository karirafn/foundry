using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.RevisionTriggerTests;

public sealed class RoundTrip
{
    [Fact]
    public void WhenReviewFeedbackTrigger_RoundTripsThroughOutboxSerializer()
    {
        // Arrange
        RevisionTrigger.ReviewFeedback trigger = new(
            Comments: [new ReviewComment("Please fix the formatting.")],
            OmittedCommentCount: 2,
            NewestConsumedCommentAt: new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

        // Act
        string json = JsonSerializer.Serialize<RevisionTrigger>(trigger, OutboxSerializerOptions.Default);
        RevisionTrigger? deserialized = JsonSerializer.Deserialize<RevisionTrigger>(json, OutboxSerializerOptions.Default);

        // Assert
        RevisionTrigger.ReviewFeedback result = deserialized.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();
        result.ShouldSatisfyAllConditions(
            () => result.Discriminator.ShouldBe("review_feedback"),
            () => result.Comments.Count.ShouldBe(1),
            () => result.Comments[0].Body.ShouldBe("Please fix the formatting."),
            () => result.OmittedCommentCount.ShouldBe(2),
            () => result.NewestConsumedCommentAt.ShouldBe(trigger.NewestConsumedCommentAt));
    }

    [Fact]
    public void WhenFailedChecksTrigger_RoundTripsThroughOutboxSerializer()
    {
        // Arrange
        RevisionTrigger.FailedChecks trigger = new(
            FailingChecks: [
                new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
                new FailingCheck("ci/lint", "failure", null),
            ]);

        // Act
        string json = JsonSerializer.Serialize<RevisionTrigger>(trigger, OutboxSerializerOptions.Default);
        RevisionTrigger? deserialized = JsonSerializer.Deserialize<RevisionTrigger>(json, OutboxSerializerOptions.Default);

        // Assert
        RevisionTrigger.FailedChecks result = deserialized.ShouldBeOfType<RevisionTrigger.FailedChecks>();
        result.ShouldSatisfyAllConditions(
            () => result.Discriminator.ShouldBe("failed_checks"),
            () => result.FailingChecks.Count.ShouldBe(2),
            () => result.FailingChecks[0].Name.ShouldBe("ci/build"),
            () => result.FailingChecks[1].Name.ShouldBe("ci/lint"),
            () => result.FailingChecks[1].DetailsUrl.ShouldBeNull());
    }

    [Fact]
    public void WhenReviewFeedbackEmbeddedInDispatchContext_RoundTripsThroughOutboxSerializer()
    {
        // Arrange
        DispatchContext.Revision revision = new(
            BranchName: "feat/42-fix-it",
            PullRequestUrl: "https://github.com/owner/repo/pull/42",
            Trigger: new RevisionTrigger.ReviewFeedback(
                Comments: [new ReviewComment("Address the nit.")],
                OmittedCommentCount: 0,
                NewestConsumedCommentAt: null));

        // Act
        string json = JsonSerializer.Serialize<DispatchContext>(revision, OutboxSerializerOptions.Default);
        DispatchContext? deserialized = JsonSerializer.Deserialize<DispatchContext>(json, OutboxSerializerOptions.Default);

        // Assert
        DispatchContext.Revision result = deserialized.ShouldBeOfType<DispatchContext.Revision>();
        result.ShouldSatisfyAllConditions(
            () => result.BranchName.ShouldBe("feat/42-fix-it"),
            () => result.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/42"),
            () => result.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>());
    }

    [Fact]
    public void WhenFailedChecksEmbeddedInDispatchContext_RoundTripsThroughOutboxSerializer()
    {
        // Arrange
        DispatchContext.Revision revision = new(
            BranchName: "feat/42-fix-it",
            PullRequestUrl: "https://github.com/owner/repo/pull/42",
            Trigger: new RevisionTrigger.FailedChecks(
                FailingChecks: [new FailingCheck("ci/test", "failure", null)]));

        // Act
        string json = JsonSerializer.Serialize<DispatchContext>(revision, OutboxSerializerOptions.Default);
        DispatchContext? deserialized = JsonSerializer.Deserialize<DispatchContext>(json, OutboxSerializerOptions.Default);

        // Assert
        DispatchContext.Revision result = deserialized.ShouldBeOfType<DispatchContext.Revision>();
        RevisionTrigger.FailedChecks fc = result.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
        fc.FailingChecks.Count.ShouldBe(1);
        fc.FailingChecks[0].Name.ShouldBe("ci/test");
    }
}
