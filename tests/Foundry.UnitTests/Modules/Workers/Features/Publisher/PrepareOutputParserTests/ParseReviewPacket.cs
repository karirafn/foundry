using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PrepareOutputParserTests;

/// <summary>
/// The review lines a Prepare for a review run prints after its <c>FOUNDRY_BASE</c> line. The line shapes are those
/// workers/tests/publisher.bats captures from publisher.sh run against real git 2.39.5.
/// </summary>
public sealed class ParseReviewPacket
{
    private const string DefaultSha = "1111111111111111111111111111111111111111";
    private const string PatchIdValue = "4bd8bd7fa5e1ac4a1e94d3b1a2f0c0a7d8b8a3e1";
    private const string BaseLine = $"FOUNDRY_BASE default_branch=main default_sha={DefaultSha}\n";

    private static readonly RunBase Base = new(CommitSha.From(DefaultSha), RunBranchHead: null);

    private static readonly PrepareOutcome MalformedPacket =
        new PrepareOutcome.Failed(new FailureReason.ContainerError(PrepareOutputParser.MalformedReviewPacketMessage));

    [Fact]
    public void WhenReviewLinesFollowTheBase_ReturnsTheChangedPathsAndPatchId()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            BaseLine
            + "FOUNDRY_REVIEW_PATH A\tnew file.txt\n"
            + "FOUNDRY_REVIEW_PATH D\tgone.txt\n"
            + "FOUNDRY_REVIEW_PATH M\tsrc/edit.cs\n"
            + $"FOUNDRY_REVIEW_PATCH_ID {PatchIdValue}\n",
            expectsRunBranch: false,
            expectsReviewPacket: true);

        // Assert
        PrepareOutcome.Prepared prepared = outcome.ShouldBeOfType<PrepareOutcome.Prepared>();
        prepared.ShouldSatisfyAllConditions(
            () => prepared.Base.ShouldBe(Base),
            () => prepared.ReviewPacket.ShouldNotBeNull().ChangedPaths.ShouldBe(
            [
                new ChangedPath(ChangeStatus.Added, "new file.txt"),
                new ChangedPath(ChangeStatus.Deleted, "gone.txt"),
                new ChangedPath(ChangeStatus.Modified, "src/edit.cs"),
            ]),
            () => prepared.ReviewPacket.ShouldNotBeNull().PatchId.ShouldBe(PatchId.From(PatchIdValue)));
    }

    [Fact]
    public void WhenThePathHoldsATab_SplitsOnTheFirstTabOnly()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            BaseLine + "FOUNDRY_REVIEW_PATH M\tdocs/a\tb.md\n" + $"FOUNDRY_REVIEW_PATCH_ID {PatchIdValue}\n",
            expectsRunBranch: false,
            expectsReviewPacket: true);

        // Assert
        outcome.ShouldBeOfType<PrepareOutcome.Prepared>().ReviewPacket.ShouldNotBeNull().ChangedPaths
            .ShouldBe([new ChangedPath(ChangeStatus.Modified, "docs/a\tb.md")]);
    }

    [Fact]
    public void WhenTheLinesCarryDockerTimestamps_ReadsPastThem()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            "2026-11-17T10:00:00.123456789Z " + BaseLine
            + "2026-11-17T10:00:00.123456789Z FOUNDRY_REVIEW_PATH M\tREADME.md\r\n"
            + $"2026-11-17T10:00:00.123456789Z FOUNDRY_REVIEW_PATCH_ID {PatchIdValue}\r\n",
            expectsRunBranch: false,
            expectsReviewPacket: true);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Prepared(
            Base,
            new ReviewPacketFacts([new ChangedPath(ChangeStatus.Modified, "README.md")], PatchId.From(PatchIdValue))));
    }

    [Fact]
    public void WhenTheReviewedCommitChangesNothing_ReturnsAnEmptyPacketWithNoPatchId()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(0, BaseLine, expectsRunBranch: false, expectsReviewPacket: true);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Prepared(Base, new ReviewPacketFacts([], patchId: null)));
    }

    [Fact]
    public void WhenPathsChangedWithoutAPatchId_ReturnsThePathsWithNoPatchId()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            BaseLine + "FOUNDRY_REVIEW_PATH M\tscript.sh\n",
            expectsRunBranch: false,
            expectsReviewPacket: true);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Prepared(
            Base,
            new ReviewPacketFacts([new ChangedPath(ChangeStatus.Modified, "script.sh")], patchId: null)));
    }

    [Fact]
    public void WhenNoReviewWasRequested_ReturnsNoPacket()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(0, BaseLine, expectsRunBranch: false, expectsReviewPacket: false);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Prepared(Base, ReviewPacket: null));
    }

    [Fact]
    public void WhenLinesLinesFollowThePaths_ReturnsEachHunksAddedLineRange()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            BaseLine
            + "FOUNDRY_REVIEW_PATH M\tedit.txt\n"
            + "FOUNDRY_REVIEW_PATH A\tnew file.txt\n"
            + "FOUNDRY_REVIEW_LINES edit.txt 2-2\n"
            + "FOUNDRY_REVIEW_LINES edit.txt 6-7\n"
            + "FOUNDRY_REVIEW_LINES new file.txt 1-1\n"
            + $"FOUNDRY_REVIEW_PATCH_ID {PatchIdValue}\n",
            expectsRunBranch: false,
            expectsReviewPacket: true);

        // Assert
        outcome.ShouldBeOfType<PrepareOutcome.Prepared>().ReviewPacket.ShouldNotBeNull().AddedLines.ShouldBe(
        [
            new AddedLineRange("edit.txt", 2, 2),
            new AddedLineRange("edit.txt", 6, 7),
            new AddedLineRange("new file.txt", 1, 1),
        ]);
    }

    [Theory]
    [InlineData("FOUNDRY_REVIEW_LINES src/a.cs 2-1\n")]
    [InlineData("FOUNDRY_REVIEW_LINES src/a.cs 0-1\n")]
    [InlineData("FOUNDRY_REVIEW_LINES src/a.cs x-y\n")]
    [InlineData("FOUNDRY_REVIEW_LINES src/a.cs 3\n")]
    [InlineData("FOUNDRY_REVIEW_LINES src/a.cs\n")]
    [InlineData("FOUNDRY_REVIEW_LINES  1-2\n")]
    [InlineData("FOUNDRY_REVIEW_LINES src/other.cs 1-2\n")]
    [InlineData("FOUNDRY_REVIEW_LINES src/gone.cs 1-2\n")]
    public void WhenALinesLineIsMalformedOrNamesNoAddedOrModifiedPath_FailsTheParse(string linesLine)
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            BaseLine
            + "FOUNDRY_REVIEW_PATH M\tsrc/a.cs\n"
            + "FOUNDRY_REVIEW_PATH D\tsrc/gone.cs\n"
            + linesLine
            + $"FOUNDRY_REVIEW_PATCH_ID {PatchIdValue}\n",
            expectsRunBranch: false,
            expectsReviewPacket: true);

        // Assert
        outcome.ShouldBe(MalformedPacket);
    }

    [Theory]
    [InlineData("FOUNDRY_REVIEW_PATH R\tmoved.txt\n")]
    [InlineData("FOUNDRY_REVIEW_PATH M\t\n")]
    [InlineData("FOUNDRY_REVIEW_PATH M src/no-tab.cs\n")]
    [InlineData("FOUNDRY_REVIEW_PATH\n")]
    [InlineData("FOUNDRY_REVIEW_PATCH_ID not-a-patch-id\n")]
    [InlineData("FOUNDRY_REVIEW_PATCH_ID 4bd8bd7fa5e1ac4a1e94d3b1a2f0c0a7d8b8a3e1 extra\n")]
    [InlineData("FOUNDRY_REVIEW_LINES src/a.cs 1-2\n")]
    [InlineData("FOUNDRY_REVIEW_PATCH_ID 4bd8bd7fa5e1ac4a1e94d3b1a2f0c0a7d8b8a3e1\nFOUNDRY_REVIEW_PATCH_ID 4bd8bd7fa5e1ac4a1e94d3b1a2f0c0a7d8b8a3e1\n")]
    [InlineData("FOUNDRY_REVIEW_PATCH_ID 4bd8bd7fa5e1ac4a1e94d3b1a2f0c0a7d8b8a3e1\n")]
    public void WhenAReviewLineIsMalformed_FailsTheParse(string reviewLines)
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            BaseLine + reviewLines,
            expectsRunBranch: false,
            expectsReviewPacket: true);

        // Assert
        outcome.ShouldBe(MalformedPacket);
    }

    [Fact]
    public void WhenReviewLinesArriveButNoReviewWasRequested_FailsTheParse()
    {
        // Act
        PrepareOutcome outcome = PrepareOutputParser.Parse(
            0,
            BaseLine + "FOUNDRY_REVIEW_PATH M\tsrc/edit.cs\n",
            expectsRunBranch: false,
            expectsReviewPacket: false);

        // Assert
        outcome.ShouldBe(MalformedPacket);
    }
}
