using System.Globalization;
using System.Text;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

/// <summary>
/// A log a provider client read, rendered into a checks-fix prompt as a dispatch would render it, and the early
/// failure lines such a log opens with.
/// </summary>
internal static class FailingJobLogPrompt
{
    public const string MarkerLine = "  failed MyTests.Adds (193ms)";
    public const string StackFrame = "    at MyTests.Adds() in /src/AddsTests.cs:line 12";

    /// <summary>The first 64 KiB of a log: setup noise, the failure and its stack frame, then dumped output.</summary>
    public static byte[] Head()
    {
        StringBuilder head = new();
        for (int index = 0; index < 600; index++)
        {
            head.Append(CultureInfo.InvariantCulture, $"restoring package {index}\n");
        }

        head.Append(MarkerLine).Append('\n').Append(StackFrame).Append('\n');
        while (head.Length < 64 * 1024)
        {
            head.Append("captured stdout line\n");
        }

        return Encoding.UTF8.GetBytes(head.ToString());
    }

    public static string Render(CiJobLog.Available log)
    {
        FailingCheck check = new("ci/test", "failure", null);
        Result<string> prompt = SystemPromptBuilder.Build(
            "Engineering principles.",
            1,
            "You are implementing issue #{issueNumber}.\n\n{issueContent}",
            new DispatchContext.Revision("feat/1-fix", "https://example.test/pull/1", new RevisionTrigger.FailedChecks([check])),
            new IssueContent("Fix", "Body.", []),
            timeoutMinutes: 120,
            toolchain: ToolchainFixtures.With(docker: false, csharpLs: false),
            failingJobLogs: [new FailingJobLog(check.Name, log.Text, log.Length, log.FailureExcerpt)]);

        return prompt.ShouldBeOfType<Result<string>.Success>().Value;
    }
}
