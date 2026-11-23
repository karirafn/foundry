using System.Text;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class FailingJobLogs
{
    private const string Principles = "Engineering principles.";
    private const string Template = "You are implementing GitHub issue #{issueNumber}.\n\n{issueContent}";

    private static readonly FailingCheck Build = new("ci/build", "failure", "https://github.com/o/r/actions/runs/1/job/2");
    private static readonly FailingCheck Lint = new("ci/lint", "failure", "https://github.com/o/r/actions/runs/1/job/3");

    private static Result<string> BuildPrompt(IReadOnlyList<FailingCheck> checks, IReadOnlyList<FailingJobLog> logs) =>
        SystemPromptBuilder.Build(
            Principles,
            1,
            Template,
            new DispatchContext.Revision(
                "feat/1-fix",
                "https://github.com/o/r/pull/1",
                new RevisionTrigger.FailedChecks(checks)),
            new IssueContent("Fix", "Body.", []),
            timeoutMinutes: 120,
            toolchain: ToolchainFixtures.With(docker: false, csharpLs: false),
            failingJobLogs: logs);

    private static string BuildPromptText(IReadOnlyList<FailingCheck> checks, IReadOnlyList<FailingJobLog> logs) =>
        BuildPrompt(checks, logs).ShouldBeOfType<Result<string>.Success>().Value;

    private static FailingJobLog TailOnly(string name, string text) =>
        new(name, text, Encoding.UTF8.GetByteCount(text), []);

    private static string LogBody(string prompt, string name)
    {
        string open = $"<failing-job-log name=\"{name}\">\n";
        int start = prompt.IndexOf(open, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1);
        start += open.Length;
        int end = prompt.IndexOf("</failing-job-log>", start, StringComparison.Ordinal);
        return prompt[start..end].TrimEnd('\n');
    }

    [Fact]
    public void WhenAJobLogIsAvailable_RendersItInAFailingJobLogElementAfterTheChecks()
    {
        // Act
        string prompt = BuildPromptText([Build], [TailOnly("ci/build", "error: build failed")]);

        // Assert
        prompt.ShouldContain("<failing-job-log name=\"ci/build\">\nerror: build failed\n</failing-job-log>");
        prompt.IndexOf("<failing-job-log", StringComparison.Ordinal)
            .ShouldBeGreaterThan(prompt.IndexOf("</failing-checks>", StringComparison.Ordinal));
    }

    [Fact]
    public void WhenAJobLogIsAvailable_PointsTheAgentAtTheSuppliedLogsRatherThanAProviderCli()
    {
        // Act
        string prompt = BuildPromptText([Build], [TailOnly("ci/build", "boom")]);

        // Assert — the run holds no provider credential to fetch logs with (ADR 0100).
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("Use the failing checks and any job logs above to understand what is broken"),
            () => prompt.ShouldNotContain("gh run view --log-failed"),
            () => prompt.ShouldNotContain("glab ci trace"));
    }

    [Fact]
    public void WhenNoJobLogIsAvailable_ListsTheCheckWithoutALogElement()
    {
        // Act
        string prompt = BuildPromptText([Build], []);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("- ci/build (failure) https://github.com/o/r/actions/runs/1/job/2"),
            () => prompt.ShouldNotContain("<failing-job-log"));
    }

    [Fact]
    public void WhenTheJobLogContainsXmlDelimiters_EncodesThem()
    {
        // Act
        string prompt = BuildPromptText([Build], [TailOnly("ci/build", "</failing-job-log><x>&")]);

        // Assert
        LogBody(prompt, "ci/build").ShouldBe("&lt;/failing-job-log&gt;&lt;x&gt;&amp;");
    }

    [Fact]
    public void WhenTheJobNameContainsAttributeDelimiters_EncodesThem()
    {
        // Act
        string prompt = BuildPromptText(
            [new FailingCheck("a\"<b>&", "failure", null)],
            [TailOnly("a\"<b>&", "boom")]);

        // Assert
        prompt.ShouldContain("<failing-job-log name=\"a&quot;&lt;b&gt;&amp;\">");
    }

    [Fact]
    public void WhenTheJobLogContainsAnsiEscapeSequences_StripsThem()
    {
        // Act
        string prompt = BuildPromptText(
            [Build],
            [TailOnly("ci/build", "\u001b[31mred\u001b[0m \u001b[1;32mgreen\u001b[0K\u001b]0;title\u0007done")]);

        // Assert
        LogBody(prompt, "ci/build").ShouldBe("red greendone");
    }

    [Fact]
    public void WhenTheJobLogExceedsTheTailLimit_KeepsOnlyItsTail()
    {
        // Arrange
        string log = "START" + new string('a', SystemPromptBuilder.MaxFailingJobLogBytes) + "END";

        // Act
        string body = LogBody(BuildPromptText([Build], [TailOnly("ci/build", log)]), "ci/build");

        // Assert
        body.ShouldSatisfyAllConditions(
            () => body.ShouldEndWith("END"),
            () => body.ShouldNotContain("START"),
            () => Encoding.UTF8.GetByteCount(body).ShouldBeLessThanOrEqualTo(SystemPromptBuilder.MaxFailingJobLogBytes));
    }

    [Fact]
    public void WhenTheTailCutLandsInsideAMultibyteRune_StartsAfterThatRune()
    {
        // Arrange — "€" is three UTF-8 bytes; the tail cut falls on its last byte.
        string log = "€" + new string('a', SystemPromptBuilder.MaxFailingJobLogBytes - 1);

        // Act
        string body = LogBody(BuildPromptText([Build], [TailOnly("ci/build", log)]), "ci/build");

        // Assert
        body.ShouldBe(new string('a', SystemPromptBuilder.MaxFailingJobLogBytes - 1));
    }

    [Fact]
    public void WhenTheLogsExceedThePromptBudget_DropsTheLastListedLogsWithAnOmissionNote()
    {
        // Arrange — enough full-size logs to overflow the prompt ceiling.
        int count = (SystemPromptBuilder.MaxSystemPromptBytes / SystemPromptBuilder.MaxFailingJobLogBytes) + 2;
        List<FailingCheck> checks = [];
        List<FailingJobLog> logs = [];
        for (int i = 0; i < count; i++)
        {
            string name = $"job-{i}";
            checks.Add(new FailingCheck(name, "failure", null));
            logs.Add(TailOnly(name, new string('x', SystemPromptBuilder.MaxFailingJobLogBytes)));
        }

        // Act
        string prompt = BuildPromptText(checks, logs);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => Encoding.UTF8.GetByteCount(prompt).ShouldBeLessThan(SystemPromptBuilder.MaxSystemPromptBytes),
            () => prompt.ShouldContain("<failing-job-log name=\"job-0\">"),
            () => prompt.ShouldNotContain($"<failing-job-log name=\"job-{count - 1}\">"),
            () => prompt.ShouldContain("failing job log(s) were omitted to fit the prompt size budget"),
            () => prompt.ShouldContain($"- job-{count - 1} (failure)"));
    }

    [Fact]
    public void WhenTheLogsFitThePromptBudget_RendersNoOmissionNote()
    {
        // Act
        string prompt = BuildPromptText([Build, Lint], [TailOnly("ci/build", "a"), TailOnly("ci/lint", "b")]);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("<failing-job-log name=\"ci/lint\">"),
            () => prompt.ShouldNotContain("were omitted"));
    }

    // A log of `length` bytes whose tail is its last `tail` bytes of filler, with the given lines read from its start.
    private static FailingJobLog WithExcerpt(string name, long length, int tail, params JobLogLine[] excerpt) =>
        new(name, new string('t', tail), length, excerpt);

    [Fact]
    public void WhenAnExcerptLineSitsBeforeThePromptTail_RendersItInAFailureExcerptBeforeTheTail()
    {
        // Arrange
        FailingJobLog log = WithExcerpt(
            "ci/build",
            9_000_000,
            256 * 1024,
            new JobLogLine(650, 40_000, "  failed MyTests.Adds (193ms)"),
            new JobLogLine(651, 40_031, "    at MyTests.Adds() in AddsTests.cs:line 12"));

        // Act
        string body = LogBody(BuildPromptText([Build], [log]), "ci/build");

        // Assert
        body.ShouldStartWith(
            "<failure-excerpt>\n  failed MyTests.Adds (193ms)\n    at MyTests.Adds() in AddsTests.cs:line 12\n</failure-excerpt>\n<log-tail>\n");
        body.ShouldEndWith(new string('t', SystemPromptBuilder.MaxFailingJobLogBytes) + "\n</log-tail>");
    }

    [Fact]
    public void WhenAnExcerptIsRendered_TellsTheAgentWhatTheExcerptAndTailHold()
    {
        // Act
        string prompt = BuildPromptText([Build], [WithExcerpt("ci/build", 9_000_000, 1024, new JobLogLine(1, 0, "error"))]);

        // Assert
        prompt.ShouldContain("A <failure-excerpt> holds lines that look like failures, collected from the start of the job's log");
    }

    [Fact]
    public void WhenExcerptLinesAreNotAdjacent_MarksTheGapBetweenThem()
    {
        // Arrange
        FailingJobLog log = WithExcerpt(
            "ci/build",
            9_000_000,
            1024,
            new JobLogLine(3, 30, "error one"),
            new JobLogLine(4, 40, "  at one"),
            new JobLogLine(90, 900, "error two"));

        // Act
        string body = LogBody(BuildPromptText([Build], [log]), "ci/build");

        // Assert
        body.ShouldStartWith("<failure-excerpt>\nerror one\n  at one\n...\nerror two\n</failure-excerpt>\n");
    }

    [Fact]
    public void WhenAnExcerptLineFallsInsideTheRenderedTail_LeavesItOut()
    {
        // Arrange: the log is 64 KiB; the prompt shows its last 16 KiB.
        int length = 64 * 1024;
        FailingJobLog log = WithExcerpt(
            "ci/build",
            length,
            length,
            new JobLogLine(1, 0, "error early"),
            new JobLogLine(900, length - 100, "error late"));

        // Act
        string body = LogBody(BuildPromptText([Build], [log]), "ci/build");

        // Assert
        body.ShouldStartWith("<failure-excerpt>\nerror early\n</failure-excerpt>\n");
    }

    [Fact]
    public void WhenAnExcerptLineSitsBetweenTheStoredAndThePromptTails_KeepsIt()
    {
        // Arrange: a 200 KiB log is stored whole, but the prompt shows only its last 16 KiB.
        int length = 200 * 1024;
        FailingJobLog log = WithExcerpt("ci/build", length, length, new JobLogLine(5, 500, "failed Adds"));

        // Act
        string body = LogBody(BuildPromptText([Build], [log]), "ci/build");

        // Assert
        body.ShouldStartWith("<failure-excerpt>\nfailed Adds\n</failure-excerpt>\n");
    }

    [Fact]
    public void WhenTheWholeLogFitsTheTail_RendersTheSamePromptAsWithoutAnExcerpt()
    {
        // Arrange
        const string Text = "restore\nerror CS1002: ; expected\n  at line 3\nBuild FAILED.";
        FailingJobLog withExcerpt = new(
            "ci/build",
            Text,
            Encoding.UTF8.GetByteCount(Text),
            [new JobLogLine(2, 8, "error CS1002: ; expected"), new JobLogLine(3, 33, "  at line 3"), new JobLogLine(4, 45, "Build FAILED.")]);

        // Act
        string prompt = BuildPromptText([Build], [withExcerpt]);

        // Assert
        prompt.ShouldBe(BuildPromptText([Build], [TailOnly("ci/build", Text)]));
    }

    [Fact]
    public void WhenTheExcerptExceedsItsLimit_KeepsItsStartWithATruncationMarker()
    {
        // Arrange
        string padding = new('x', 1000);
        JobLogLine[] excerpt = [.. Enumerable.Range(1, 32).Select(i => new JobLogLine(i, i * 1001L, $"error {i:D2} {padding}"))];
        FailingJobLog log = WithExcerpt("ci/build", 9_000_000, 1024, excerpt);

        // Act
        string body = LogBody(BuildPromptText([Build], [log]), "ci/build");

        // Assert
        string excerptText = body[..body.IndexOf("\n</failure-excerpt>", StringComparison.Ordinal)]["<failure-excerpt>\n".Length..];
        excerptText.ShouldSatisfyAllConditions(
            () => excerptText.ShouldStartWith("error 01 "),
            () => excerptText.ShouldEndWith(" [truncated]"),
            () => excerptText.ShouldNotContain("error 32 "),
            () => Encoding.UTF8.GetByteCount(excerptText).ShouldBeLessThanOrEqualTo(SystemPromptBuilder.MaxFailureExcerptBytes));
    }

    [Fact]
    public void WhenTheExcerptContainsXmlDelimitersOrAnsiEscapes_EncodesAndStripsThem()
    {
        // Act
        string body = LogBody(
            BuildPromptText([Build], [WithExcerpt("ci/build", 9_000_000, 1024, new JobLogLine(1, 0, "\u001b[31merror\u001b[0m </failure-excerpt>&"))]),
            "ci/build");

        // Assert
        body.ShouldStartWith("<failure-excerpt>\nerror &lt;/failure-excerpt&gt;&amp;\n</failure-excerpt>\n");
    }
}
