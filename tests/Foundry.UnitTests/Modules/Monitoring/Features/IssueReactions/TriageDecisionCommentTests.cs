using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Features.IssueReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.IssueReactions;

public sealed class TriageDecisionCommentTests
{
    private const string ShortenedNote = "_Decision text is shortened to fit one comment._";

    [Fact]
    public void Compose_WhenQueued_ListsEveryDecisionInOrderBetweenTheOpeningAndClosingLines()
    {
        // Arrange
        List<TriageDecision> decisions =
        [
            TriageDecisionBuilder.Build(question: "Which retry cap?", decision: "Three attempts."),
            TriageDecisionBuilder.Build(
                question: "Which log level?", decision: "Warning.", ground: TriageDecisionGround.ImplementationDetail),
        ];

        // Act
        string comment = TriageDecisionComment.Compose(decisions, TriageRelease.Queued, ImplementDemand.Demanding, null);

        // Assert
        comment.ShouldBe(string.Join(
            "\n",
            "Triage decided these details and queued the issue:",
            "- `Which retry cap?`: `Three attempts.` (Settled by existing behaviour)",
            "- `Which log level?`: `Warning.` (Implementation detail)",
            "Implementation tier: **demanding**",
            "Use **Re-triage** to revisit them before work starts."));
    }

    [Fact]
    public void Compose_WhenWaitingOnBlockers_SaysTheIssueWaitsOnItsBlockers()
    {
        // Arrange
        List<TriageDecision> decisions = [TriageDecisionBuilder.Build()];

        // Act
        string comment = TriageDecisionComment.Compose(decisions, TriageRelease.WaitingOnBlockers, ImplementDemand.Demanding, null);

        // Assert
        comment.Split('\n')[0].ShouldBe("Triage decided these details; the issue waits on its open blockers before it is queued:");
    }

    [Fact]
    public void Compose_RendersModelTextAsLiteralTextOnOneLine()
    {
        // Arrange
        List<TriageDecision> decisions =
        [
            TriageDecisionBuilder.Build(question: "Who owns `x`?", decision: "@team owns it.\n# Heading\n| a | b |"),
        ];

        // Act
        string comment = TriageDecisionComment.Compose(decisions, TriageRelease.Queued, ImplementDemand.Demanding, null);

        // Assert
        comment.Split('\n')[1].ShouldBe("- ``Who owns `x`?``: `@team owns it. # Heading | a | b |` (Settled by existing behaviour)");
    }

    [Fact]
    public void Compose_WhenUnderTheLimit_KeepsEveryDecisionWholeWithoutANote()
    {
        // Arrange
        string longDecision = new('d', TriageDecision.DecisionMaxLength);
        List<TriageDecision> decisions = [.. Enumerable.Range(1, 20).Select(_ => TriageDecisionBuilder.Build(decision: longDecision))];

        // Act
        string comment = TriageDecisionComment.Compose(decisions, TriageRelease.Queued, ImplementDemand.Demanding, null);

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldContain($"`{longDecision}`"),
            () => comment.ShouldNotContain(ShortenedNote));
    }

    [Fact]
    public void Compose_WhenOverTheProviderLimit_ShortensDecisionTextButNotQuestionsAndNotesIt()
    {
        // Arrange
        List<TriageDecision> decisions =
        [
            .. Enumerable.Range(1, 20).Select(index => TriageDecisionBuilder.Build(
                question: $"Question {index} {new string('q', 100)}",
                decision: new string('`', TriageDecision.DecisionMaxLength))),
        ];

        // Act
        string comment = TriageDecisionComment.Compose(decisions, TriageRelease.Queued, ImplementDemand.Demanding, "Touches the outbox.");

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.Length.ShouldBeLessThanOrEqualTo(60_000),
            () => comment.ShouldContain(ShortenedNote),
            () => comment.ShouldContain("…"),
            () => decisions.ShouldAllBe(decision => comment.Contains($"`{decision.Question}`")),
            () => comment.Split('\n')[^2].ShouldBe("Implementation tier: **demanding** — `Touches the outbox.`"),
            () => comment.Split('\n')[^1].ShouldBe("Use **Re-triage** to revisit them before work starts."));
    }

    [Fact]
    public void Compose_WhenRoutineWithAReason_StatesTheTierAndTheReasonBeforeTheClosingLine()
    {
        // Arrange
        List<TriageDecision> decisions = [TriageDecisionBuilder.Build()];

        // Act
        string comment = TriageDecisionComment.Compose(
            decisions, TriageRelease.WaitingOnBlockers, ImplementDemand.Routine, "Copies the existing slice.");

        // Assert
        comment.Split('\n')[^2].ShouldBe("Implementation tier: **routine** — `Copies the existing slice.`");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_WhenTheReasonIsMissing_StatesTheTierWithoutADash(string? reason)
    {
        // Arrange
        List<TriageDecision> decisions = [TriageDecisionBuilder.Build()];

        // Act
        string comment = TriageDecisionComment.Compose(decisions, TriageRelease.Queued, ImplementDemand.Routine, reason);

        // Assert
        comment.Split('\n')[^2].ShouldBe("Implementation tier: **routine**");
    }

    [Theory]
    [InlineData("none")]
    [InlineData(" None ")]
    public void Compose_WhenTheReasonIsNone_SaysNoDemandingTrigger(string reason)
    {
        // Arrange
        List<TriageDecision> decisions = [TriageDecisionBuilder.Build()];

        // Act
        string comment = TriageDecisionComment.Compose(decisions, TriageRelease.Queued, ImplementDemand.Demanding, reason);

        // Assert
        comment.Split('\n')[^2].ShouldBe("Implementation tier: **demanding** — no demanding trigger");
    }

    [Fact]
    public void Compose_RendersTheReasonAsLiteralTextOnOneLine()
    {
        // Arrange
        List<TriageDecision> decisions = [TriageDecisionBuilder.Build()];

        // Act
        string comment = TriageDecisionComment.Compose(
            decisions, TriageRelease.Queued, ImplementDemand.Demanding, "Edits `auth`\n# Heading");

        // Assert
        comment.Split('\n')[^2].ShouldBe("Implementation tier: **demanding** — ``Edits `auth` # Heading``");
    }
}
