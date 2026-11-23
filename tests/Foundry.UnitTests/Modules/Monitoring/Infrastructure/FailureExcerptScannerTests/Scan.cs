using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.FailureExcerptScannerTests;

public sealed class Scan
{
    private static IReadOnlyList<JobLogLine> ScanText(string text, int chunkSize = 7)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        FailureExcerptScanner scanner = new();
        for (int start = 0; start < bytes.Length; start += chunkSize)
        {
            scanner.Append(bytes.AsSpan(start, Math.Min(chunkSize, bytes.Length - start)));
        }

        return scanner.Complete();
    }

    private static string Lines(int count, string prefix) =>
        string.Concat(Enumerable.Range(1, count).Select(index => $"{prefix} {index}\n"));

    [Fact]
    public void TheScanCapIs8MiB() => FailureExcerptScanner.ScanBytes.ShouldBe(8 * 1024 * 1024);

    [Fact]
    public void TheExcerptCapIs32KiB() => FailureExcerptScanner.MaxExcerptBytes.ShouldBe(32 * 1024);

    [Theory]
    [InlineData("  failed MyTests.Adds (193ms)")]
    [InlineData("Build FAILED.")]
    [InlineData("Test failure: expected 1")]
    [InlineData("error CS1002: ; expected")]
    [InlineData("Unhandled exception. System.Exception: boom")]
    [InlineData("panic: runtime error")]
    public void WhenALineHoldsAMarkerWord_KeepsIt(string line)
    {
        // Act
        IReadOnlyList<JobLogLine> excerpt = ScanText($"restore\n{line}\n");

        // Assert
        excerpt.Select(kept => kept.Text).ShouldBe([line]);
    }

    [Theory]
    [InlineData("errors: 0")]
    [InlineData("System.InvalidOperationException: boom")]
    [InlineData("Passed! 12 tests")]
    public void WhenALineHoldsAMarkerOnlyInsideAWord_SkipsIt(string line)
    {
        // Act
        IReadOnlyList<JobLogLine> excerpt = ScanText($"{line}\n");

        // Assert
        excerpt.ShouldBeEmpty();
    }

    [Fact]
    public void WhenAMarkerLineIsFound_KeepsItAndTheTenLinesAfterIt()
    {
        // Arrange
        string log = "start\nfailed Adds\n" + Lines(12, "   at Frame");

        // Act
        IReadOnlyList<JobLogLine> excerpt = ScanText(log);

        // Assert
        excerpt.Select(line => line.Text).ShouldBe(["failed Adds", .. Enumerable.Range(1, 10).Select(i => $"   at Frame {i}")]);
    }

    [Fact]
    public void WhenAMarkerLineIsKept_RecordsItsLineNumberAndByteOffset()
    {
        // Arrange: 'é' is two bytes.
        string log = "é\nr\nerror here\n";

        // Act
        JobLogLine line = ScanText(log).ShouldHaveSingleItem();

        // Assert
        line.ShouldBe(new JobLogLine(3, 5, "error here"));
    }

    [Fact]
    public void WhenALineEndsWithACarriageReturn_DropsIt()
    {
        // Act
        IReadOnlyList<JobLogLine> excerpt = ScanText("error one\r\n");

        // Assert
        excerpt.ShouldHaveSingleItem().Text.ShouldBe("error one");
    }

    [Fact]
    public void WhenTheLastLineHasNoNewline_StillScansIt()
    {
        // Act
        IReadOnlyList<JobLogLine> excerpt = ScanText("ok\npanic: at the end");

        // Assert
        excerpt.ShouldHaveSingleItem().Text.ShouldBe("panic: at the end");
    }

    [Fact]
    public void WhenTheLogStartsWithAByteOrderMark_SkipsIt()
    {
        // Act
        IReadOnlyList<JobLogLine> excerpt = ScanText("﻿error first\n");

        // Assert
        excerpt.ShouldHaveSingleItem().ShouldBe(new JobLogLine(1, 3, "error first"));
    }

    [Fact]
    public void WhenTheExcerptIsFull_KeepsTheEarliestMatches()
    {
        // Arrange: each marker line is about 1 KiB, so 40 of them overflow the 32 KiB cap.
        string padding = new('x', 1000);
        string log = string.Concat(Enumerable.Range(1, 40).Select(index => $"error {index:D2} {padding}\n"));

        // Act
        IReadOnlyList<JobLogLine> excerpt = ScanText(log, chunkSize: 4096);

        // Assert
        excerpt.Sum(line => Encoding.UTF8.GetByteCount(line.Text) + 1).ShouldBeLessThanOrEqualTo(FailureExcerptScanner.MaxExcerptBytes);
        excerpt[0].Text.ShouldStartWith("error 01 ");
        excerpt.Select(line => line.Number).ShouldBe(Enumerable.Range(1, excerpt.Count).Select(number => (long)number));
        excerpt.Count.ShouldBeLessThan(40);
    }

    [Fact]
    public void WhenAMarkerLineSitsPastTheScanCap_SkipsIt()
    {
        // Arrange
        FailureExcerptScanner scanner = new();
        byte[] filler = Encoding.UTF8.GetBytes(new string('x', 1023) + "\n");
        for (int written = 0; written < FailureExcerptScanner.ScanBytes; written += filler.Length)
        {
            scanner.Append(filler);
        }

        // Act
        scanner.Append("error too late\n"u8);
        IReadOnlyList<JobLogLine> excerpt = scanner.Complete();

        // Assert
        excerpt.ShouldBeEmpty();
        scanner.IsDone.ShouldBeTrue();
    }

    [Fact]
    public void WhenALineIsVeryLong_KeepsOnlyItsStart()
    {
        // Act
        JobLogLine line = ScanText("error " + new string('x', 100_000) + "\nnext\n", chunkSize: 4096)[0];

        // Assert
        Encoding.UTF8.GetByteCount(line.Text).ShouldBeLessThanOrEqualTo(FailureExcerptScanner.MaxLineBytes);
        line.Text.ShouldStartWith("error x");
    }
}
