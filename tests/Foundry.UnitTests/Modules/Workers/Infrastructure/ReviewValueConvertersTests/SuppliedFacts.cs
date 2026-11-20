using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Infrastructure.Configurations;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.ReviewValueConvertersTests;

public sealed class SuppliedFacts
{
    private static readonly PullRequestUrl Url = PullRequestUrl.From("https://github.com/acme/app/pull/7");
    private static readonly CommitSha Commit = CommitSha.From("0123456789abcdef0123456789abcdef01234567");

    [Fact]
    public void WhenStoredAndRead_KeepsTheAddedLines()
    {
        // Arrange
        SuppliedReviewFacts facts = SuppliedReviewFacts.Compute(
            Url,
            Commit,
            new ReviewPacketFacts(
                [new ChangedPath(ChangeStatus.Modified, "src/a.cs")],
                patchId: null,
                [new AddedLineRange("src/a.cs", 3, 5)]),
            issueBody: null,
            checksPassed: false);

        // Act
        SuppliedReviewFacts read = ReviewValueConverters.DeserializeFacts(ReviewValueConverters.SerializeFacts(facts));

        // Assert
        read.AddedLines.ShouldBe([new AddedLineRange("src/a.cs", 3, 5)]);
    }

    [Fact]
    public void WhenStoredBeforeAddedLinesWereReported_ReadsNoAddedLines()
    {
        // Arrange
        const string stored = """
            {"pullRequestUrl":"https://github.com/acme/app/pull/7","reviewedCommit":"0123456789abcdef0123456789abcdef01234567","patchId":null,"paths":[{"status":"modified","path":"src/a.cs","class":"production"}],"criteria":[]}
            """;

        // Act
        SuppliedReviewFacts read = ReviewValueConverters.DeserializeFacts(stored);

        // Assert
        read.ShouldSatisfyAllConditions(
            () => read.ClassifiedPaths.Count.ShouldBe(1),
            () => read.AddedLines.ShouldBeEmpty());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenStoredAndRead_KeepsWhetherChecksPassed(bool checksPassed)
    {
        // Arrange
        SuppliedReviewFacts facts = SuppliedReviewFacts.Compute(
            Url,
            Commit,
            new ReviewPacketFacts([new ChangedPath(ChangeStatus.Modified, "src/a.cs")], patchId: null),
            issueBody: null,
            checksPassed);

        // Act
        SuppliedReviewFacts read = ReviewValueConverters.DeserializeFacts(ReviewValueConverters.SerializeFacts(facts));

        // Assert
        read.ShouldSatisfyAllConditions(
            () => read.ChecksPassed.ShouldBe(checksPassed),
            () => read.ShouldBe(facts));
    }

    [Fact]
    public void WhenStoredBeforeTheChecksResultWasSupplied_ReadsChecksAsNotPassed()
    {
        // Arrange
        const string stored = """
            {"pullRequestUrl":"https://github.com/acme/app/pull/7","reviewedCommit":"0123456789abcdef0123456789abcdef01234567","patchId":null,"paths":[{"status":"modified","path":"src/a.cs","class":"production"}],"criteria":[],"addedLines":[]}
            """;

        // Act
        SuppliedReviewFacts read = ReviewValueConverters.DeserializeFacts(stored);

        // Assert
        read.ChecksPassed.ShouldBeFalse();
    }
}
