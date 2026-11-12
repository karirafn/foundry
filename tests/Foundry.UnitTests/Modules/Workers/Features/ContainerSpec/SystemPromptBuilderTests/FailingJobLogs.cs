using System.Text;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;

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
            installsDocker: false,
            installsDotnet: false,
            failingJobLogs: logs);

    private static string BuildPromptText(IReadOnlyList<FailingCheck> checks, IReadOnlyList<FailingJobLog> logs) =>
        BuildPrompt(checks, logs).ShouldBeOfType<Result<string>.Success>().Value;

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
        string prompt = BuildPromptText([Build], [new FailingJobLog("ci/build", "error: build failed")]);

        // Assert
        prompt.ShouldContain("<failing-job-log name=\"ci/build\">\nerror: build failed\n</failing-job-log>");
        prompt.IndexOf("<failing-job-log", StringComparison.Ordinal)
            .ShouldBeGreaterThan(prompt.IndexOf("</failing-checks>", StringComparison.Ordinal));
    }

    [Fact]
    public void WhenAJobLogIsAvailable_PointsTheAgentAtTheSuppliedLogsRatherThanAProviderCli()
    {
        // Act
        string prompt = BuildPromptText([Build], [new FailingJobLog("ci/build", "boom")]);

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
        string prompt = BuildPromptText([Build], [new FailingJobLog("ci/build", "</failing-job-log><x>&")]);

        // Assert
        LogBody(prompt, "ci/build").ShouldBe("&lt;/failing-job-log&gt;&lt;x&gt;&amp;");
    }

    [Fact]
    public void WhenTheJobNameContainsAttributeDelimiters_EncodesThem()
    {
        // Act
        string prompt = BuildPromptText(
            [new FailingCheck("a\"<b>&", "failure", null)],
            [new FailingJobLog("a\"<b>&", "boom")]);

        // Assert
        prompt.ShouldContain("<failing-job-log name=\"a&quot;&lt;b&gt;&amp;\">");
    }

    [Fact]
    public void WhenTheJobLogContainsAnsiEscapeSequences_StripsThem()
    {
        // Act
        string prompt = BuildPromptText(
            [Build],
            [new FailingJobLog("ci/build", "\u001b[31mred\u001b[0m \u001b[1;32mgreen\u001b[0K\u001b]0;title\u0007done")]);

        // Assert
        LogBody(prompt, "ci/build").ShouldBe("red greendone");
    }

    [Fact]
    public void WhenTheJobLogExceedsTheTailLimit_KeepsOnlyItsTail()
    {
        // Arrange
        string log = "START" + new string('a', SystemPromptBuilder.MaxFailingJobLogBytes) + "END";

        // Act
        string body = LogBody(BuildPromptText([Build], [new FailingJobLog("ci/build", log)]), "ci/build");

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
        string body = LogBody(BuildPromptText([Build], [new FailingJobLog("ci/build", log)]), "ci/build");

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
            logs.Add(new FailingJobLog(name, new string('x', SystemPromptBuilder.MaxFailingJobLogBytes)));
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
        string prompt = BuildPromptText([Build, Lint], [new FailingJobLog("ci/build", "a"), new FailingJobLog("ci/lint", "b")]);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("<failing-job-log name=\"ci/lint\">"),
            () => prompt.ShouldNotContain("were omitted"));
    }
}
