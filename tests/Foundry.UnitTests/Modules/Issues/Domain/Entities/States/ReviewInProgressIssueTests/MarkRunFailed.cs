using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewInProgressIssueTests;

public sealed class MarkRunFailed
{
    [Fact]
    public void WhenTransientWithARetryLeft_ReturnsReviewFailed()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().ReviewInProgress();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        Issue next = reviewing.MarkRunFailed("API overloaded", FailureCategory.TransientApiError, 1, failedAt);

        // Assert
        ReviewFailedIssue failed = next.ShouldBeOfType<ReviewFailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureReason.ShouldBe("API overloaded"),
            () => failed.FailureCategory.ShouldBe(FailureCategory.TransientApiError),
            () => failed.FailedAt.ShouldBe(failedAt),
            () => failed.Stage.ShouldBe(IssueStage.Review),
            () => failed.NeedsAction.ShouldBeFalse(),
            () => reviewing.DomainEvents.OfType<IssueReviewFailed>().ShouldHaveSingleItem());
    }

    [Fact]
    public void WhenTransientWithTheScheduleSpent_SkipsTheReviewToAPerson()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithAutoMerge(true).ReviewInProgress();
        int maxRetries = FailureCategory.TransientApiError.AutomaticRetry!.MaxRetries;

        // Act
        Issue next = reviewing.MarkRunFailed("API overloaded", FailureCategory.TransientApiError, maxRetries, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ReviewOutcome
            .ShouldBe(new ReviewOutcome.Skipped(FailureCategory.TransientApiErrorToken));
    }

    [Fact]
    public void WhenNotTransient_SkipsTheReviewToAPersonAndNeverMerges()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithAutoMerge(true).ReviewInProgress();

        // Act
        Issue next = reviewing.MarkRunFailed("Verdict missing", FailureCategory.VerdictInvalid, 0, DateTimeOffset.UtcNow);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.ReviewOutcome.ShouldBe(new ReviewOutcome.Skipped(FailureCategory.VerdictInvalidToken)),
            () => reviewing.DomainEvents.OfType<IssueInReview>().ShouldHaveSingleItem(),
            () => reviewing.DomainEvents.OfType<IssueMergePending>().ShouldBeEmpty());
    }
}
