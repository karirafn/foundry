using System.Text;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class TrackerContextShedding
{
    private const string Principles = "PRINCIPLES_MARKER Engineering principles.";
    private const string Template = "You are on issue #{issueNumber}.\n\n{issueContent}\n\nWork on it.";
    private const int ReferencedIssueCount = 10;
    private const int FullBodyBytes = 16 * 1024;
    private const int BodyRunLength = 100;

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IssueContent IssueWithComments(int commentCount) =>
        new(
            "Fix the bug",
            "The bug body.",
            Enumerable.Range(1, commentCount)
                .Select(i => new IssueContentComment(i, "bob", $"COMMENT_{i} text", CreatedAt.AddHours(i)))
                .ToList());

    private static string BodyOf(int number) => new((char)('a' + number - 1), FullBodyBytes);

    private static string BodyRun(int number) => new((char)('a' + number - 1), BodyRunLength);

    private static ReferencedIssue Referenced(int number) =>
        new(
            number,
            IssueRelation.Mention,
            $"Referenced title {number}",
            true,
            [],
            ClosingMergeRequestState.None,
            BodyOf(number));

    private static TrackerContext ContextWithReferencedIssues(int count, IReadOnlyList<OpenIssue>? openIssues = null) =>
        new(
            Enumerable.Range(1, count).Select(Referenced).ToList(),
            [],
            false,
            openIssues ?? []);

    private static string Build(
        IssueContent issue,
        TrackerContext? trackerContext,
        DispatchContext? context = null,
        IReadOnlyList<FailingJobLog>? failingJobLogs = null,
        IReadOnlyList<Clarification>? clarifications = null) =>
        SystemPromptBuilder.Build(
            Principles,
            42,
            Template,
            context ?? new DispatchContext.Fresh("feat/42-fix-the-bug"),
            issue,
            timeoutMinutes: 120,
            installsDocker: false,
            installsDotnet: false,
            failingJobLogs: failingJobLogs,
            clarifications: clarifications,
            trackerContext: trackerContext)
            .ShouldBeOfType<Result<string>.Success>().Value;

    private static IReadOnlyList<OpenIssue> OpenIssues(int count, int titleLength) =>
        Enumerable.Range(1, count).Select(number => new OpenIssue(number, new string('t', titleLength))).ToList();

    private static Clarification Round(string? input, int dayOffset = 0)
    {
        DateTimeOffset askedAt = CreatedAt.AddDays(dayOffset);
        InputQuestion[] questions = [new InputQuestion("Which webhooks?", "Two kinds exist.", null)];

        return input is null
            ? Clarification.Ask(questions, askedAt)
            : new Clarification(questions, input, askedAt, askedAt.AddHours(1));
    }

    private static List<string> RenderedInputs(string prompt) =>
        prompt.Split("<input>")
            .Skip(1)
            .Select(section => section[..section.IndexOf("</input>", StringComparison.Ordinal)])
            .ToList();

    private static int KeptBodyCount(string prompt) =>
        Enumerable.Range(1, ReferencedIssueCount).Count(number => prompt.Contains(BodyRun(number), StringComparison.Ordinal));

    [Fact]
    public void Build_WhenReferencedIssueBodiesOverflowTheBudget_DropsBodiesFromTheLastSelectedBackAndKeepsEveryComment()
    {
        // Arrange
        IssueContent issue = IssueWithComments(3);
        TrackerContext trackerContext = ContextWithReferencedIssues(ReferencedIssueCount);

        // Act
        string prompt = Build(issue, trackerContext);

        // Assert
        int kept = KeptBodyCount(prompt);
        int dropped = ReferencedIssueCount - kept;
        prompt.ShouldSatisfyAllConditions(
            () => Encoding.UTF8.GetByteCount(prompt).ShouldBeLessThan(SystemPromptBuilder.MaxSystemPromptBytes),
            () => kept.ShouldBeInRange(1, ReferencedIssueCount - 1),
            () => Enumerable.Range(1, kept)
                .ShouldAllBe(number => prompt.Contains(BodyRun(number), StringComparison.Ordinal)),
            () => Enumerable.Range(1, ReferencedIssueCount)
                .ShouldAllBe(number => prompt.Contains($"<title>Referenced title {number}</title>", StringComparison.Ordinal)),
            () => prompt.ShouldContain(
                $"Note: the bodies of {dropped} referenced issue(s) were omitted to fit the prompt size budget."),
            () => Enumerable.Range(1, 3)
                .ShouldAllBe(number => prompt.Contains($"COMMENT_{number} text", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_WhenTheIndexFitsOnceTheBodiesAreDropped_KeepsTheIndexWholeAndShedsBodiesFirst()
    {
        // Arrange
        IssueContent issue = IssueWithComments(1);
        TrackerContext trackerContext = ContextWithReferencedIssues(ReferencedIssueCount, OpenIssues(100, 200));

        // Act
        string prompt = Build(issue, trackerContext);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => KeptBodyCount(prompt).ShouldBeLessThan(ReferencedIssueCount),
            () => prompt.ShouldContain("<open-issue number=\"100\">"),
            () => prompt.ShouldNotContain("the open-issue list was omitted"));
    }

    [Fact]
    public void Build_WhenTheIndexAloneOverflowsTheBudget_DropsItBehindANote()
    {
        // Arrange
        IssueContent issue = IssueWithComments(1);
        TrackerContext trackerContext = ContextWithReferencedIssues(ReferencedIssueCount, OpenIssues(100, 1_500));

        // Act
        string prompt = Build(issue, trackerContext);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => Encoding.UTF8.GetByteCount(prompt).ShouldBeLessThan(SystemPromptBuilder.MaxSystemPromptBytes),
            () => prompt.ShouldNotContain("<open-issues>"),
            () => prompt.ShouldContain("Note: the open-issue list was omitted to fit the prompt size budget."));
    }

    [Fact]
    public void Build_WhenTheCommentsOverflowTheBudget_DropsTheOldestCommentWhateverTheTrackerBodiesHold()
    {
        // Arrange
        IssueContent issue = new(
            "Fix the bug",
            "The bug body.",
            Enumerable.Range(1, 3)
                .Select(i => new IssueContentComment(i, "bob", $"COMMENT_{i} " + new string('c', 50_000), CreatedAt.AddHours(i)))
                .ToList());
        TrackerContext withBodies = ContextWithReferencedIssues(ReferencedIssueCount, OpenIssues(100, 200));
        TrackerContext atMinimum = withBodies with
        {
            ReferencedIssues = withBodies.ReferencedIssues.Select(referenced => referenced with { Body = string.Empty }).ToList(),
            OpenIssues = [],
        };

        // Act
        string prompt = Build(issue, withBodies);
        string promptAtMinimum = Build(issue, atMinimum);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => Encoding.UTF8.GetByteCount(prompt).ShouldBeLessThan(SystemPromptBuilder.MaxSystemPromptBytes),
            () => prompt.ShouldNotContain("COMMENT_1 "),
            () => prompt.ShouldContain("COMMENT_3 "),
            () => promptAtMinimum.ShouldNotContain("COMMENT_1 "),
            () => promptAtMinimum.ShouldContain("COMMENT_3 "),
            () => prompt.Contains("COMMENT_2 ", StringComparison.Ordinal)
                .ShouldBe(promptAtMinimum.Contains("COMMENT_2 ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_WhenReviewFeedbackAndTheTrackerContextOverflow_RendersTheFeedbackWholeWhileShedding()
    {
        // Arrange
        // Bodies shorter than the reserve let a tracker that overdraws it show.
        TrackerContext trackerContext = WithBodiesOfLength(ContextWithReferencedIssues(ReferencedIssueCount), 300);
        int largestWholeLength = LargestFeedbackLengthRenderedWhole(WithBodiesOfLength(trackerContext, 1));
        DispatchContext.Revision revision = FeedbackRevision(largestWholeLength);

        // Act
        string prompt = Build(IssueWithComments(1), trackerContext, revision);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => KeptBodyCount(prompt).ShouldBeLessThan(ReferencedIssueCount),
            () => FeedbackRenderedWhole(prompt, largestWholeLength).ShouldBeTrue());
    }

    private static DispatchContext.Revision FeedbackRevision(int commentLength) =>
        new(
            "feat/42-fix",
            "https://github.com/org/repo/pull/42",
            new RevisionTrigger.ReviewFeedback(
                Enumerable.Range(1, 3)
                    .Select(i => new ReviewComment($"Feedback {i}: {new string('f', commentLength)}"))
                    .ToList(),
                0,
                null));

    private static bool FeedbackRenderedWhole(string prompt, int commentLength) =>
        Enumerable.Range(1, 3)
            .All(i => prompt.Contains($"Feedback {i}: {new string('f', commentLength)}", StringComparison.Ordinal));

    private static TrackerContext WithBodiesOfLength(TrackerContext trackerContext, int length) =>
        trackerContext with
        {
            ReferencedIssues = trackerContext.ReferencedIssues
                .Select(referenced => referenced with { Body = new string(BodyOf(referenced.Number)[0], length) })
                .ToList(),
        };

    // The payload fills the room the prompt leaves, so it is sized by measuring built output: the longest feedback
    // that still renders whole beside the given context.
    private static int LargestFeedbackLengthRenderedWhole(TrackerContext trackerContext)
    {
        int whole = 1;
        int overflowing = 100_000;

        while (overflowing - whole > 1)
        {
            int length = (whole + overflowing) / 2;
            string prompt = Build(IssueWithComments(1), trackerContext, FeedbackRevision(length));

            if (FeedbackRenderedWhole(prompt, length))
            {
                whole = length;
            }
            else
            {
                overflowing = length;
            }
        }

        return whole;
    }

    [Fact]
    public void Build_WhenFailingChecksAndTheTrackerContextOverflow_RendersTheJobLogsWholeWhileShedding()
    {
        // Arrange
        string[] logTails = Enumerable.Range(0, 4).Select(i => new string((char)('P' + i), 16 * 1024)).ToArray();
        List<FailingJobLog> jobLogs = logTails.Select((tail, i) => new FailingJobLog($"ci/job-{i}", tail)).ToList();
        List<FailingCheck> checks = jobLogs.Select(log => new FailingCheck(log.Name, "failure", null)).ToList();
        DispatchContext.Revision revision = new(
            "feat/42-fix",
            "https://github.com/org/repo/pull/42",
            new RevisionTrigger.FailedChecks(checks));
        TrackerContext trackerContext = ContextWithReferencedIssues(ReferencedIssueCount);

        // Act
        string prompt = Build(IssueWithComments(1), trackerContext, revision, jobLogs);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => KeptBodyCount(prompt).ShouldBeLessThan(ReferencedIssueCount),
            () => logTails.ShouldAllBe(tail => prompt.Contains(tail, StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_WhenTheLogOverflows_CutsOlderInputsOnlyAfterTheTrackerContextAndTheCommentsAreShed()
    {
        // Arrange
        string olderInput = new('o', 100_000);
        string newestInput = new('n', 20_000);
        IssueContent issue = new(
            "Fix the bug",
            "BODY_MARKER whole.",
            [new IssueContentComment(1, "bob", "COMMENT_1 text", CreatedAt)]);
        TrackerContext trackerContext = ContextWithReferencedIssues(ReferencedIssueCount, OpenIssues(100, 200));

        // Act
        string prompt = Build(issue, trackerContext, clarifications: [Round(olderInput), Round(newestInput, 1)]);

        // Assert
        List<string> inputs = RenderedInputs(prompt);
        prompt.ShouldSatisfyAllConditions(
            () => KeptBodyCount(prompt).ShouldBe(0),
            () => prompt.ShouldNotContain("<open-issues>"),
            () => prompt.ShouldNotContain("COMMENT_1 text"),
            () => prompt.ShouldContain("BODY_MARKER whole."),
            () => inputs[0].ShouldEndWith(" [truncated]"),
            () => inputs[1].ShouldBe(newestInput));
    }
}
