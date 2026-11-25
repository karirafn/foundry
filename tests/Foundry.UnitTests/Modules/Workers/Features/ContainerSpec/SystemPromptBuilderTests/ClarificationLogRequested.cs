using System.Text;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class ClarificationLogRequested
{
    private const string Principles = "Engineering principles.";
    private const string Template = "You are implementing GitHub issue #{issueNumber}.\n\n{issueContent}";
    private const string RestrictedTemplate = "You are reviewing GitHub issue #{issueNumber}.\n\n{issueContent}";
    private static readonly IssueContent Issue = new("Add retry", "Retry webhook delivery.", [], SpecHashFixtures.Any);
    private static readonly DateTimeOffset At = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static readonly Clarification Requested = Clarification.Request(
        [new ReviewComment("Date should be a <date picker> & nothing else.", "src/Form.cs", 12), new ReviewComment("Thanks.")],
        0,
        At);

    private static readonly Clarification AnsweredAsked =
        Clarification.Ask([StructuredQuestionBuilder.Build(question: "Which database?")], At)
            .Answer(ClarificationInput.FromComments("ok"), At.AddHours(1));

    private static string Build(DispatchContext context, params Clarification[] log) =>
        SystemPromptBuilder.Build(
            Principles,
            1,
            Template,
            context,
            Issue,
            timeoutMinutes: 120,
            toolchain: ToolchainFixtures.With(docker: false, csharpLs: false),
            clarifications: log).ShouldBeOfType<Result<string>.Success>().Value;

    private static string BuildRestricted(params Clarification[] log) =>
        SystemPromptBuilder.BuildRestricted(
            Principles,
            1,
            RestrictedTemplate,
            Issue,
            ToolchainFixtures.With(csharpLs: false),
            log).ShouldBeOfType<Result<string>.Success>().Value;

    public static TheoryData<string> PromptKinds() => ["build", "restricted"];

    private static string PromptOf(string kind, params Clarification[] log) =>
        kind == "build" ? Build(new DispatchContext.Fresh("feat/1-fix"), log) : BuildRestricted(log);

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void ARequestedRound_RendersItsCommentsXmlEncodedWithPathLineAndReceivedTime(string kind)
    {
        // Act
        string prompt = PromptOf(kind, Requested);

        // Assert
        prompt.ShouldContain(
            $"<clarification received-at=\"{At.UtcDateTime:O}\">\n<review-comments>On src/Form.cs:12:\n"
            + "Date should be a &lt;date picker&gt; &amp; nothing else.\n\nThanks.</review-comments>\n</clarification>");
    }

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void ARequestedRoundWithOmittedComments_SaysHowManyWereOmitted(string kind)
    {
        // Act
        string prompt = PromptOf(kind, Clarification.Request([new ReviewComment("Rename it.")], 7, At));

        // Assert
        prompt.ShouldContain("</review-comments>\n7 more comments in this batch were omitted.\n</clarification>");
    }

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void ALogWithOnlyARequestedRound_DoesNotSayARunStoppedToAskAPerson(string kind)
    {
        // Act
        string prompt = PromptOf(kind, Requested);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldNotContain("stopped to ask a person"),
            () => prompt.ShouldContain(SystemPromptBuilder.RequestedRoundRule));
    }

    [Fact]
    public void ALogWithAskedAndRequestedRounds_SpeaksOfTheQuestionsBelowNotTheseQuestions()
    {
        // Act
        string prompt = Build(new DispatchContext.Fresh("feat/1-fix"), AnsweredAsked, Requested);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldNotContain("Earlier runs stopped to ask a person these questions"),
            () => prompt.ShouldContain("Earlier runs stopped to ask a person the questions below."),
            () => prompt.ShouldContain(SystemPromptBuilder.RequestedRoundRule));
    }

    [Fact]
    public void ALogWithoutARequestedRound_DoesNotDescribeReviewComments()
    {
        // Act
        string prompt = Build(new DispatchContext.Fresh("feat/1-fix"), AnsweredAsked);

        // Assert
        prompt.ShouldNotContain(SystemPromptBuilder.RequestedRoundRule);
    }

    public static TheoryData<string, string> LogsOfEachKind() =>
        new()
        {
            { "build", "asked" },
            { "build", "decided" },
            { "build", "requested" },
            { "build", "mixed" },
            { "restricted", "asked" },
            { "restricted", "requested" },
            { "restricted", "mixed" },
        };

    [Theory]
    [MemberData(nameof(LogsOfEachKind))]
    public void EveryLog_StatesTheNewestPersonInputPrecedenceRuleOnce(string kind, string logKind)
    {
        // Arrange
        Clarification decided = Clarification.Decide([TriageDecisionBuilder.Build()], At);
        Clarification[] log = logKind switch
        {
            "asked" => [AnsweredAsked],
            "decided" => [decided],
            "requested" => [Requested],
            _ => [AnsweredAsked, decided, Requested],
        };

        // Act
        string prompt = PromptOf(kind, log);

        // Assert
        Occurrences(prompt, SystemPromptBuilder.PersonInputPrecedenceRule).ShouldBe(1);
    }

    [Fact]
    public void WhenARunResumesAfterAQuestion_ItStillSeesTheRequestedRound()
    {
        // Arrange
        DispatchContext resumed = new DispatchContext.Revision(
            "feat/1-fix",
            "https://github.com/o/r/pull/1",
            new RevisionTrigger.InputProvided());

        // Act
        string prompt = Build(resumed, Requested, AnsweredAsked);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(SystemPromptBuilder.InputProvidedSectionMarker),
            () => prompt.ShouldContain("Date should be a &lt;date picker&gt; &amp; nothing else."),
            () => prompt.ShouldContain($"<clarification received-at=\"{At.UtcDateTime:O}\">"));
    }

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void WhenTheLogMustBeCutToFit_OlderRoundsAreCutBeforeTheNewestRequestedRound(string kind)
    {
        // Arrange
        string huge = new('x', SystemPromptBuilder.MaxSystemPromptBytes);
        Clarification olderRequested = Clarification.Request([new ReviewComment("O" + huge, "src/Old.cs", 1)], 4, At);
        Clarification olderAsked = Clarification.Ask([StructuredQuestionBuilder.Build()], At.AddHours(1))
            .Answer(ClarificationInput.FromComments("A" + huge), At.AddHours(2));
        Clarification newestRequested = Clarification.Request([new ReviewComment("N" + huge, "src/New.cs", 2)], 0, At.AddHours(3));

        // Act
        string prompt = PromptOf(kind, olderRequested, olderAsked, newestRequested);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => Encoding.UTF8.GetByteCount(prompt).ShouldBeLessThan(SystemPromptBuilder.MaxSystemPromptBytes),
            () => prompt.ShouldContain(
                $"<clarification received-at=\"{At.UtcDateTime:O}\">\n<review-comments> [truncated]</review-comments>\n"
                + "4 more comments in this batch were omitted.\n</clarification>"),
            () => prompt.ShouldContain("<input> [truncated]</input>"),
            () => prompt.ShouldContain("On src/New.cs:2:\nNxxxx"),
            () => prompt.ShouldContain("x [truncated]</review-comments>"));
    }

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void WhenTheLogMustBeCutToFit_AnOlderRequestedRoundIsCutBeforeTheNewestAnsweredRound(string kind)
    {
        // Arrange
        string huge = new('x', SystemPromptBuilder.MaxSystemPromptBytes);
        Clarification olderRequested = Clarification.Request([new ReviewComment("O" + huge)], 0, At);
        Clarification newestAsked = Clarification.Ask([StructuredQuestionBuilder.Build()], At.AddHours(1))
            .Answer(ClarificationInput.FromComments("A" + huge), At.AddHours(2));

        // Act
        string prompt = PromptOf(kind, olderRequested, newestAsked);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("<review-comments> [truncated]</review-comments>"),
            () => prompt.ShouldContain("<input>Axxxx"));
    }
}
