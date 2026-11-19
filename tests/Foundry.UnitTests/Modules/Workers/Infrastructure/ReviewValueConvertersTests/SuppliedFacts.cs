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
            issueBody: null);

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
}
