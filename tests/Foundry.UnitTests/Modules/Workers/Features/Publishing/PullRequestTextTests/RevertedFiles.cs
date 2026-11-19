using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class RevertedFiles
{
    private static readonly PullRequestDescription Description = Descriptions.Create();

    private static readonly RevertedFile[] Reverted =
    [
        new("assets/demo.mp4", RevertedFileReason.Oversized, 50_540_134),
        new("tools/protoc.exe", RevertedFileReason.Binary, 940),
    ];

    private const string Section = """
        <details><summary>Reverted files (2)</summary>

        - `assets/demo.mp4`: too large (48.2 MB)
        - `tools/protoc.exe`: binary (940 B)

        </details>
        """;

    [Fact]
    public void WhenFilesWereReverted_ThePullRequestBodyListsThemCollapsedBeforeTheClosesLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, null, Description, "Demo", 42, [], Reverted, []);

        // Assert
        text.Body.ShouldEndWith($"- dotnet test: 12 passed.\n\n{Section}\n\nCloses #42");
    }

    [Fact]
    public void WhenFilesWereReverted_TheSummaryCommentListsThemCollapsed()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], Reverted, []);

        // Assert
        comment.ShouldEndWith($"- dotnet test: 12 passed.\n\n{Section}");
    }

    [Fact]
    public void WhenNoFilesWereReverted_TheBodyHasNoRevertedSection()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], [], []);

        // Assert
        comment.ShouldNotContain("Reverted files");
    }

    [Fact]
    public void APathHoldingABacktick_StaysOneCodeSpan()
    {
        // Act
        string comment = PullRequestText.SummaryComment(
            null,
            Description,
            [],
            [new RevertedFile("odd`name.bin", RevertedFileReason.Binary, 1024)],
            []);

        // Assert
        comment.ShouldContain("\n- ``odd`name.bin``: binary (1 KB)\n");
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
            null,
            Description,
            [],
            [new RevertedFile("f.bin", RevertedFileReason.Binary, sizeBytes)],
            []);

        // Assert
        comment.ShouldContain($"\n- `f.bin`: binary ({expected})\n");
    }
}
