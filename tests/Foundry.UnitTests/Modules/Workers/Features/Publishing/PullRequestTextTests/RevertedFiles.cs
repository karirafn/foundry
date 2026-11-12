using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class RevertedFiles
{
    private static readonly PullRequestDescription Description = PullRequestDescription.TryCreate("Adds the demo.")!;

    private static readonly RevertedFile[] Reverted =
    [
        new("assets/demo.mp4", RevertedFileReason.Oversized, 50_540_134),
        new("tools/protoc.exe", RevertedFileReason.Binary, 940),
    ];

    private const string Section = """
        ## Files reverted by Foundry

        Foundry's diff gate reverted these files in a separate commit before pushing, because binary files and files over its size limit are not published. Add them yourself if they belong in the change.

        - `assets/demo.mp4`: too large (48.2 MB)
        - `tools/protoc.exe`: binary (940 B)
        """;

    [Fact]
    public void WhenFilesWereReverted_ThePullRequestBodyListsThemBeforeTheClosesLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, null, Description, "Demo", 42, [], Reverted);

        // Assert
        text.Body.ShouldBe($"Adds the demo.\n\n{Section}\n\nCloses #42");
    }

    [Fact]
    public void WhenFilesWereReverted_TheSummaryCommentListsThem()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], Reverted);

        // Assert
        comment.ShouldBe($"Adds the demo.\n\n{Section}");
    }

    [Fact]
    public void WhenNoFilesWereReverted_TheBodyHasNoRevertedSection()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], []);

        // Assert
        comment.ShouldNotContain("Files reverted by Foundry");
    }

    [Fact]
    public void APathHoldingABacktick_StaysOneCodeSpan()
    {
        // Act
        string comment = PullRequestText.SummaryComment(
            Description,
            [],
            [new RevertedFile("odd`name.bin", RevertedFileReason.Binary, 1024)]);

        // Assert
        comment.ShouldEndWith("- ``odd`name.bin``: binary (1 KB)");
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(12_697, "12.4 KB")]
    [InlineData(3_250_585, "3.1 MB")]
    [InlineData(5_368_709_120, "5 GB")]
    public void TheSizeIsReadable(long sizeBytes, string expected)
    {
        // Act
        string comment = PullRequestText.SummaryComment(
            Description,
            [],
            [new RevertedFile("f.bin", RevertedFileReason.Binary, sizeBytes)]);

        // Assert
        comment.ShouldEndWith($"- `f.bin`: binary ({expected})");
    }
}
