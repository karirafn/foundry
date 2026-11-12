using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

// Foundry publishes worker runs (ADR 0100): the agent commits, and no implement prompt may still ask it to push, open a
// pull request or watch checks.
public sealed class PublishingInstructions
{
    private const string Principles = "PRINCIPLES_MARKER Engineering principles.";
    private const string Template = "You are implementing issue #{issueNumber}.\n\n{issueContent}";
    private static readonly IssueContent Issue = new("Fix the bug", "The bug body.", []);

    public static TheoryData<string> Variants => [.. ContextsByVariant.Keys];

    private static readonly IReadOnlyDictionary<string, DispatchContext> ContextsByVariant =
        new Dictionary<string, DispatchContext>(StringComparer.Ordinal)
        {
            ["fresh"] = new DispatchContext.Fresh("feat/1-fix"),
            ["continuation"] = new DispatchContext.Continuation("feat/1-fix", "Container timed out."),
            ["review-feedback"] = new DispatchContext.Revision(
                "feat/1-fix",
                "https://github.com/org/repo/pull/1",
                new RevisionTrigger.ReviewFeedback([new ReviewComment("Please add tests.")], 0, null)),
            ["failed-checks"] = new DispatchContext.Revision(
                "feat/1-fix",
                "https://github.com/org/repo/pull/1",
                new RevisionTrigger.FailedChecks([new FailingCheck("ci/build", "failure", null)])),
            ["rebase-conflict"] = new DispatchContext.Revision(
                "feat/1-fix",
                "https://github.com/org/repo/pull/1",
                new RevisionTrigger.RebaseConflict()),
            ["input-provided"] = new DispatchContext.Revision(
                "feat/1-fix",
                "https://github.com/org/repo/pull/1",
                new RevisionTrigger.InputProvided()),
        };

    private static readonly string[] RemovedPhrases =
    [
        "gh pr checks",
        "glab ci status",
        "gh run view",
        "glab ci trace",
        "git fetch origin",
        "fix push",
        "push to the same branch",
        "push your changes",
        "push the branch",
        "check out and push",
        "do not create a new PR",
        "do not create a new one",
        "open (or reuse) the pull request",
        "drive the PR's checks green",
        "drive its checks green",
        "watch its checks",
        "Branch restriction",
        "Push only to the branch Foundry provides",
        "force push",
        "force-push",
        "rewrite git history",
        "Do not post comments, reviews, or replies on your own pull request",
    ];

    [Theory]
    [MemberData(nameof(Variants))]
    public void WhenAnyImplementPromptIsBuilt_ItCarriesNoPushPullRequestOrChecksInstruction(string variant)
    {
        // Act
        string prompt = BuildPrompt(variant);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            RemovedPhrases
                .Select<string, Action>(phrase => () => prompt.ShouldNotContain(phrase, Case.Insensitive))
                .ToArray());
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void WhenAnyImplementPromptIsBuilt_ItTellsTheAgentToCommitAndThatFoundryPublishes(string variant)
    {
        // Act
        string prompt = BuildPrompt(variant);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(SystemPromptBuilder.PublishingSectionMarker),
            () => prompt.ShouldContain("Commit your work on the provided branch"),
            () => prompt.ShouldContain("Foundry pushes the branch and opens or updates the pull request"));
    }

    [Fact]
    public void WhenBuilt_ThePreambleKeepsOnlyTheScopeRestriction()
    {
        // Assert
        SystemPromptBuilder.SafetyPreamble.ShouldSatisfyAllConditions(
            () => SystemPromptBuilder.SafetyPreamble.ShouldContain("- Scope restriction: Only modify files relevant to the issue."),
            () => SystemPromptBuilder.SafetyPreamble.ShouldNotContain("push", Case.Insensitive),
            () => SystemPromptBuilder.SafetyPreamble.ShouldNotContain("pull request", Case.Insensitive),
            () => SystemPromptBuilder.SafetyPreamble.ShouldNotContain("history", Case.Insensitive));
    }

    [Fact]
    public void WhenInstallsDocker_ThePrePrSectionHasNoPushOrPullRequestWording()
    {
        // Act
        string section = PrePrSection.Extract(BuildPrompt("fresh", installsDocker: true));

        // Assert
        section.ShouldSatisfyAllConditions(
            () => section.ShouldNotContain("push", Case.Insensitive),
            () => section.ShouldNotContain("pull request", Case.Insensitive),
            () => section.ShouldNotContain("pull-request", Case.Insensitive),
            () => section.ShouldNotContain(" PR", Case.Sensitive));
    }

    [Fact]
    public void WhenInstallsDocker_ThePrePrSectionKeepsTheLocalTestGateAndTheCommitFirstRule()
    {
        // Act
        string section = PrePrSection.Extract(BuildPrompt("fresh", installsDocker: true));

        // Assert
        int commitIndex = section.IndexOf(PrePrSection.CommitInstruction, StringComparison.Ordinal);
        int exitIndex = section.IndexOf(PrePrSection.NothingToPublishExit, StringComparison.Ordinal);
        int testIndex = section.IndexOf("run the repository's full test suite", StringComparison.Ordinal);
        section.ShouldSatisfyAllConditions(
            () => commitIndex.ShouldBeGreaterThan(-1),
            () => exitIndex.ShouldBeGreaterThan(commitIndex),
            () => testIndex.ShouldBeGreaterThan(exitIndex),
            () => section.ShouldContain("DOCKER_HOST"));
    }

    [Fact]
    public void WhenBuilt_ThePublishingSectionCarriesTheTimeoutBudget()
    {
        // Act
        Result<string> result = SystemPromptBuilder.Build(
            Principles, 1, Template, ContextsByVariant["fresh"], Issue,
            timeoutMinutes: 90, installsDocker: false, installsDotnet: false);
        string prompt = result.ShouldBeOfType<Result<string>.Success>().Value;

        // Assert
        prompt.ShouldContain("roughly 90 minutes of wall-clock budget");
    }

    private static string BuildPrompt(string variant, bool installsDocker = true)
    {
        Result<string> result = SystemPromptBuilder.Build(
            Principles,
            1,
            Template,
            ContextsByVariant[variant],
            Issue,
            timeoutMinutes: 120,
            installsDocker: installsDocker,
            installsDotnet: true,
            clarifications: [new Clarification([new InputQuestion("Which?", "Context.", "This one.")], "This one.", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)]);

        return result.ShouldBeOfType<Result<string>.Success>().Value;
    }
}

/// <summary>Cuts the pre-PR section out of a built prompt: from its marker to the publishing section that follows it.</summary>
internal static class PrePrSection
{
    internal const string CommitInstruction =
        "First, commit every change you made for the issue on the provided branch, leaving out scratch and generated files.";

    internal const string NothingToPublishExit =
        "If the branch still has no commits beyond the default branch after that commit, there is nothing to publish: report that and exit.";

    public static string Extract(string prompt)
    {
        int start = prompt.IndexOf(SystemPromptBuilder.PrePrSectionMarker, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1);
        int end = prompt.IndexOf(SystemPromptBuilder.PublishingSectionMarker, start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return prompt[start..end];
    }
}
