using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.SuppliedReviewFactsTests;

public sealed class EqualsChecksPassed
{
    private static SuppliedReviewFacts Facts(bool checksPassed) =>
        SuppliedReviewFacts.Compute(
            PullRequestUrl.From("https://github.com/acme/app/pull/7"),
            CommitSha.From("0123456789abcdef0123456789abcdef01234567"),
            new ReviewPacketFacts([new ChangedPath(ChangeStatus.Modified, "src/a.cs")], patchId: null),
            issueBody: null,
            checksPassed);

    [Fact]
    public void WhenOnlyTheChecksResultDiffers_TheFactsAreNotEqual()
    {
        // Act / Assert
        Facts(checksPassed: true).ShouldNotBe(Facts(checksPassed: false));
    }

    [Fact]
    public void WhenTheChecksResultIsTheSame_TheFactsAreEqual()
    {
        // Act / Assert
        Facts(checksPassed: true).ShouldBe(Facts(checksPassed: true));
    }
}
