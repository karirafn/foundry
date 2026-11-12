using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PublishOutputParserTests;

public sealed class Parse
{
    private const string Sha = "3333333333333333333333333333333333333333";

    [Fact]
    public void APublishedLine_YieldsTheHeadCountAndCommitSubjects()
    {
        // Arrange
        string log = $"""
            fetching...
            FOUNDRY_RUN_COMMIT {Sha} feat: add retry
            2026-11-11T12:00:00.123Z FOUNDRY_RUN_COMMIT {Sha} fix: handle timeout
            FOUNDRY_PUBLISH result=published head={Sha} commits=4
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Published>().ShouldSatisfyAllConditions(
            published => published.Head.ShouldBe(CommitSha.From(Sha)),
            published => published.BranchCommitCount.ShouldBe(4),
            published => published.CommitSubjects.ShouldBe(["feat: add retry", "fix: handle timeout"]));
    }

    [Fact]
    public void NothingToPublish_IsRead()
    {
        // Act
        PublishResult result = PublishOutputParser.Parse(0, "FOUNDRY_PUBLISH result=nothing_to_publish\n");

        // Assert
        result.ShouldBeOfType<PublishResult.NothingToPublish>();
    }

    [Fact]
    public void ADivergedLine_YieldsBothBranches()
    {
        // Act
        PublishResult result = PublishOutputParser.Parse(
            0,
            $"FOUNDRY_PUBLISH result=diverged branch=feat/42-x side_branch=feat/42-x-run-abc head={Sha}\n");

        // Assert
        result.ShouldBeOfType<PublishResult.Diverged>().ShouldSatisfyAllConditions(
            diverged => diverged.Branch.ShouldBe(BranchName.From("feat/42-x")),
            diverged => diverged.SideBranch.ShouldBe(BranchName.From("feat/42-x-run-abc")));
    }

    [Theory]
    [InlineData("transient", true)]
    [InlineData("permanent", false)]
    public void AFailedLine_KeepsItsKindAndTheWholeReason(string kind, bool transient)
    {
        // Act
        PublishResult result = PublishOutputParser.Parse(
            1,
            $"FOUNDRY_PUBLISH result=failed kind={kind} reason=could not reach the repository: timed out\n");

        // Assert
        result.ShouldBeOfType<PublishResult.Failed>().ShouldSatisfyAllConditions(
            failed => failed.Transient.ShouldBe(transient),
            failed => failed.Reason.ShouldBe("could not reach the repository: timed out"));
    }

    [Fact]
    public void ATokenInTheReason_IsRedacted()
    {
        // Act
        PublishResult result = PublishOutputParser.Parse(
            1,
            "FOUNDRY_PUBLISH result=failed kind=permanent reason=auth failed for ghp_abcdefghijklmnopqrstuvwxyz0123456789\n");

        // Assert
        result.ShouldBeOfType<PublishResult.Failed>().Reason.ShouldNotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789");
    }

    [Fact]
    public void NoResultLine_AfterACrash_IsATransientFailure()
    {
        // Act
        PublishResult result = PublishOutputParser.Parse(137, "killed\n");

        // Assert
        result.ShouldBeOfType<PublishResult.Failed>().Transient.ShouldBeTrue();
    }

    [Fact]
    public void TwoResultLines_AreNotTrusted()
    {
        // Arrange
        string log = $"""
            FOUNDRY_PUBLISH result=nothing_to_publish
            FOUNDRY_PUBLISH result=published head={Sha} commits=1
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Failed>().Reason.ShouldBe(PublishOutputParser.MissingResultMessage);
    }
}
