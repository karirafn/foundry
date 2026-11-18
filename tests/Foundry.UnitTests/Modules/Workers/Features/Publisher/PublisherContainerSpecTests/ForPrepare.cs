using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PublisherContainerSpecTests;

public sealed class ForPrepare
{
    private const string ReviewedSha = "5555555555555555555555555555555555555555";

    [Fact]
    public void AReviewPrepare_SetsTheReviewBranchAndCommit()
    {
        // Arrange
        PrepareRequest request = Request(new ReviewTarget(BranchName.From("feat/42-add-retry"), CommitSha.From(ReviewedSha)));

        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForPrepare(request);

        // Assert
        spec.ShouldSatisfyAllConditions(
            () => spec.EnvironmentVariables["REVIEW_BRANCH"].ShouldBe("feat/42-add-retry"),
            () => spec.EnvironmentVariables["REVIEW_COMMIT"].ShouldBe(ReviewedSha),
            () => spec.EnvironmentVariables.ShouldNotContainKey("RUN_BRANCH"),
            () => spec.Command.ShouldBe(["prepare"]));
    }

    [Fact]
    public void APrepareWithoutAReview_LeavesTheReviewVariablesUnset()
    {
        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForPrepare(Request(review: null));

        // Assert
        spec.ShouldSatisfyAllConditions(
            () => spec.EnvironmentVariables.ShouldNotContainKey("REVIEW_BRANCH"),
            () => spec.EnvironmentVariables.ShouldNotContainKey("REVIEW_COMMIT"));
    }

    private static PrepareRequest Request(ReviewTarget? review) =>
        new(
            WorkerRunId.New(),
            new Uri("https://github.com/owner/repo.git"),
            "ghp_token",
            RunBranch: null,
            new WorkerResourceLimitsSnapshot(2048, 2, 512),
            review);
}
