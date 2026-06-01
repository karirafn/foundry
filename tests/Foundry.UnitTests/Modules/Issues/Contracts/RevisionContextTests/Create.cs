using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.RevisionContextTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HoldsProvidedValues()
    {
        // Arrange
        ReviewComment comment = new("Needs a null check", FilePath: "src/Service.cs", Line: 10);

        // Act
        RevisionContext context = new(
            BranchName: "foundry/42",
            PullRequestUrl: "https://github.com/org/repo/pull/7",
            Comments: [comment]);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.BranchName.ShouldBe("foundry/42"),
            () => context.PullRequestUrl.ShouldBe("https://github.com/org/repo/pull/7"),
            () => context.Comments.Count.ShouldBe(1),
            () => context.Comments[0].ShouldBe(comment));
    }
}
