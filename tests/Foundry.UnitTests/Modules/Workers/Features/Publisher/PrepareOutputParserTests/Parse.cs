using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PrepareOutputParserTests;

public sealed class Parse
{
    private const string DefaultSha = "1111111111111111111111111111111111111111";
    private const string BranchSha = "2222222222222222222222222222222222222222";

    private static readonly PrepareOutcome MissingBase =
        new PrepareOutcome.Failed(new FailureReason.ContainerError(PrepareOutputParser.MissingBaseMessage));

    [Fact]
    public void WhenTheBaseLineReportsTheDefaultBranch_ReturnsItsHead()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            $"some git noise\nFOUNDRY_BASE default_branch=main default_sha={DefaultSha}\n",
            expectsRunBranch: false);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Prepared(new RunBase(CommitSha.From(DefaultSha), RunBranchHead: null)));
    }

    [Fact]
    public void WhenTheLineCarriesADockerTimestamp_ReadsPastIt()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            $"2026-11-11T10:00:00.123456789Z FOUNDRY_BASE default_branch=main default_sha={DefaultSha} branch_sha={BranchSha}\r\n",
            expectsRunBranch: true);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Prepared(new RunBase(CommitSha.From(DefaultSha), CommitSha.From(BranchSha))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("FOUNDRY_BASE default_branch=main\n")]
    [InlineData("FOUNDRY_BASE default_sha=not-a-sha\n")]
    [InlineData("FOUNDRY_BASE default_sha=1111111111111111111111111111111111111111 default_sha=1111111111111111111111111111111111111111\n")]
    [InlineData("echo FOUNDRY_BASE default_sha=1111111111111111111111111111111111111111\n")]
    [InlineData("FOUNDRY_BASE default_sha=1111111111111111111111111111111111111111\nFOUNDRY_BASE default_sha=1111111111111111111111111111111111111111\n")]
    public void WhenExitZeroWithoutOneValidBaseLine_FailsAsAContainerError(string log)
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(0, log, expectsRunBranch: false);

        // Assert
        outcome.ShouldBe(MissingBase);
    }

    [Fact]
    public void WhenARunBranchWasExpectedButNotReported_FailsAsAContainerError()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(0, $"FOUNDRY_BASE default_sha={DefaultSha}\n", expectsRunBranch: true);

        // Assert
        outcome.ShouldBe(MissingBase);
    }

    [Fact]
    public void WhenARunBranchWasReportedButNotExpected_FailsAsAContainerError()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            $"FOUNDRY_BASE default_sha={DefaultSha} branch_sha={BranchSha}\n",
            expectsRunBranch: false);

        // Assert
        outcome.ShouldBe(MissingBase);
    }

    [Fact]
    public void WhenNonZeroWithTheSentinel_FailsTheBootstrapWithItsDetail()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            1,
            "2026-11-11T10:00:00Z FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=the repository has no default branch to clone (is it empty?)\n",
            expectsRunBranch: false);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Failed(new FailureReason.WorkerBootstrapFailed(
            "FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=the repository has no default branch to clone (is it empty?)")));
    }

    [Fact]
    public void WhenTheSentinelCarriesASecret_ItIsRedacted()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            1,
            "FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=fatal: unable to access 'https://ghp_abcdefghijklmnopqrstuvwxyz0123456789@github.com/o/r.git'\n",
            expectsRunBranch: false);

        // Assert
        outcome.ShouldBeOfType<PrepareOutcome.Failed>().Reason.Summary.ShouldNotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789");
    }

    [Fact]
    public void WhenNonZeroWithoutTheSentinel_FailsAsAContainerErrorNamingTheExitCode()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(137, null, expectsRunBranch: false);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Failed(new FailureReason.ContainerError(
            "The Publisher exited with code 137 before preparing the run's base.")));
    }
}
