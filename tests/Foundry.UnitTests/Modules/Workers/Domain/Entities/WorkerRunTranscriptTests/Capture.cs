using System.Text;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.WorkerRunTranscriptTests;

public sealed class Capture
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenTranscriptIsUnderTheCap_StoresItWhole()
    {
        // Arrange
        const string rendered = "line one\nline two";

        // Act
        WorkerRunTranscript transcript = WorkerRunTranscript.Capture(WorkerRunId.New(), rendered, CapturedAt);

        // Assert
        transcript.Transcript.ShouldBe(rendered);
    }

    [Fact]
    public void WhenTranscriptIsExactlyAtTheCap_StoresItWholeWithNoMarker()
    {
        // Arrange — a multi-byte character so the cap is measured in UTF-8 bytes, not chars
        string head = "é\n";
        string rendered = head + new string('x', WorkerRunTranscript.MaxTranscriptBytes - Encoding.UTF8.GetByteCount(head));

        // Act
        WorkerRunTranscript transcript = WorkerRunTranscript.Capture(WorkerRunId.New(), rendered, CapturedAt);

        // Assert
        transcript.Transcript.ShouldBe(rendered);
    }

    [Fact]
    public void WhenTranscriptCrossesTheCap_KeepsHeadAndTailOnLineBoundariesWithAMarkerBetween()
    {
        // Arrange — 4,000 numbered lines of ~100 bytes is ~400 KB, well past the 256 KB cap
        string[] lines = [.. Enumerable.Range(0, 4_000).Select(i => $"line {i:D5} " + new string('é', 44))];
        string rendered = string.Join('\n', lines);

        // Act
        WorkerRunTranscript transcript = WorkerRunTranscript.Capture(WorkerRunId.New(), rendered, CapturedAt);

        // Assert
        string[] kept = transcript.Transcript.Split('\n');
        int markerIndex = Array.FindIndex(kept, line => line.Contains("omitted", StringComparison.Ordinal));
        string[] head = kept[..markerIndex];
        string[] tail = kept[(markerIndex + 1)..];
        int omittedLines = lines.Length - head.Length - tail.Length;
        transcript.ShouldSatisfyAllConditions(
            () => Encoding.UTF8.GetByteCount(transcript.Transcript).ShouldBeLessThanOrEqualTo(WorkerRunTranscript.MaxTranscriptBytes),
            () => markerIndex.ShouldBeGreaterThan(0),
            () => head.ShouldBe(lines[..head.Length]),
            () => tail.ShouldBe(lines[^tail.Length..]),
            () => tail.ShouldNotBeEmpty(),
            () => kept[markerIndex].ShouldContain($"{omittedLines} transcript lines"));
    }

    [Fact]
    public void WhenCaptured_RecordsTheRunAndCaptureTime()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();

        // Act
        WorkerRunTranscript transcript = WorkerRunTranscript.Capture(runId, "text", CapturedAt);

        // Assert
        transcript.ShouldSatisfyAllConditions(
            () => transcript.Id.ShouldBe(runId),
            () => transcript.CapturedAt.ShouldBe(CapturedAt));
    }
}
