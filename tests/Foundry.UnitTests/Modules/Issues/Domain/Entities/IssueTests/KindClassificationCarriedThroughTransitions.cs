using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class KindClassificationCarriedThroughTransitions
{
    public static TheoryData<string> Terminals =>
    [
        nameof(IssueBuilder.FreshQueued),
        "Blocked",
        "Unblocked",
        nameof(IssueBuilder.InProgress),
        nameof(IssueBuilder.Review),
        nameof(IssueBuilder.RevisionQueued),
        nameof(IssueBuilder.RevisionInProgress),
        nameof(IssueBuilder.Failed),
        nameof(IssueBuilder.ContinuableFailed),
        nameof(IssueBuilder.ContinuableFailedFromReview),
        nameof(IssueBuilder.RevisionFailed),
        nameof(IssueBuilder.RebasePending),
        nameof(IssueBuilder.RebasePendingFromRevision),
        nameof(IssueBuilder.ChecksPending),
        nameof(IssueBuilder.ChecksPendingFromRevision),
        nameof(IssueBuilder.ChecksPendingFromFixChecks),
        nameof(IssueBuilder.ReviewFromChecksPending),
        nameof(IssueBuilder.Unchanged),
        nameof(IssueBuilder.Completed),
        "BlockedFromQueued",
        "RetriedFromFailed",
        "ContinuationQueued",
        "RetriedFreshFromContinuableFailed",
        "RetriedFromUnchanged",
        "RetriedFromRevisionFailed",
    ];

    [Theory]
    [MemberData(nameof(Terminals))]
    public void WhenIssueTransitions_CarriesLabelledClassification(string terminal)
    {
        // Arrange
        KindClassification labelled = new KindClassification.Labelled(IssueKind.Bug);
        IssueBuilder builder = new IssueBuilder().WithKindClassification(labelled);

        // Act
        Issue issue = Build(builder, terminal);

        // Assert
        issue.KindClassification.ShouldBe(labelled);
    }

    private static Issue Build(IssueBuilder builder, string terminal) => terminal switch
    {
        nameof(IssueBuilder.FreshQueued) => builder.FreshQueued(),
        "Blocked" => builder.Detected().Block([42]),
        "Unblocked" => builder.Detected().Block([42]).Unblock(DateTimeOffset.UtcNow),
        nameof(IssueBuilder.InProgress) => builder.InProgress(),
        nameof(IssueBuilder.Review) => builder.Review(),
        nameof(IssueBuilder.RevisionQueued) => builder.RevisionQueued(),
        nameof(IssueBuilder.RevisionInProgress) => builder.RevisionInProgress(),
        nameof(IssueBuilder.Failed) => builder.Failed(),
        nameof(IssueBuilder.ContinuableFailed) => builder.ContinuableFailed(),
        nameof(IssueBuilder.ContinuableFailedFromReview) => builder.ContinuableFailedFromReview(),
        nameof(IssueBuilder.RevisionFailed) => builder.RevisionFailed(),
        nameof(IssueBuilder.RebasePending) => builder.RebasePending(),
        nameof(IssueBuilder.RebasePendingFromRevision) => builder.RebasePendingFromRevision(),
        nameof(IssueBuilder.ChecksPending) => builder.ChecksPending(),
        nameof(IssueBuilder.ChecksPendingFromRevision) => builder.ChecksPendingFromRevision(),
        nameof(IssueBuilder.ChecksPendingFromFixChecks) => builder.ChecksPendingFromFixChecks(),
        nameof(IssueBuilder.ReviewFromChecksPending) => builder.ReviewFromChecksPending(),
        nameof(IssueBuilder.Unchanged) => builder.Unchanged(),
        nameof(IssueBuilder.Completed) => builder.Completed(),
        "BlockedFromQueued" => builder.FreshQueued().Block([42],
        DateTimeOffset.UtcNow),
        "RetriedFromFailed" => builder.Failed().Retry(DateTimeOffset.UtcNow),
        "ContinuationQueued" => builder.ContinuableFailed().Retry(DateTimeOffset.UtcNow),
        "RetriedFreshFromContinuableFailed" => builder.ContinuableFailed().RetryFresh(DateTimeOffset.UtcNow),
        "RetriedFromUnchanged" => builder.Unchanged().Retry(DateTimeOffset.UtcNow),
        "RetriedFromRevisionFailed" => builder.RevisionFailed().Retry(DateTimeOffset.UtcNow),
        _ => throw new ArgumentOutOfRangeException(nameof(terminal), terminal, null),
    };
}
