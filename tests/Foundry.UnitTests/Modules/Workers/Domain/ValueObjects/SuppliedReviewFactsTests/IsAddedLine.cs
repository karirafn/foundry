using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.SuppliedReviewFactsTests;

public sealed class IsAddedLine
{
    private static readonly SuppliedReviewFacts Facts = SuppliedReviewFacts.Compute(
        PullRequestUrl.From("https://github.com/acme/app/pull/7"),
        CommitSha.From("0123456789abcdef0123456789abcdef01234567"),
        new ReviewPacketFacts(
            [new ChangedPath(ChangeStatus.Modified, "src/a.cs"), new ChangedPath(ChangeStatus.Deleted, "src/gone.cs")],
            patchId: null,
            [new AddedLineRange("src/a.cs", 10, 12), new AddedLineRange("src/a.cs", 40, 40)]),
        issueBody: null,
        checksPassed: false);

    [Theory]
    [InlineData("src/a.cs", 10)]
    [InlineData("src/a.cs", 12)]
    [InlineData("src/a.cs", 40)]
    public void WhenTheLineIsInsideAnAddedRange_IsTrue(string path, int line)
    {
        // Act / Assert
        Facts.IsAddedLine(path, line).ShouldBeTrue();
    }

    [Theory]
    [InlineData("src/a.cs", 13)]
    [InlineData("src/a.cs", 39)]
    [InlineData("src/gone.cs", 1)]
    [InlineData("src/other.cs", 10)]
    public void WhenTheLineIsOutsideEveryAddedRange_IsFalse(string path, int line)
    {
        // Act / Assert
        Facts.IsAddedLine(path, line).ShouldBeFalse();
    }
}
