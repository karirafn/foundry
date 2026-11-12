using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.ImplementVerdictTests;

public sealed class TryParse
{
    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void WhenOutcomeIsDoneWithAPullRequest_ParsesADoneVerdictWithTheSummaryAndDescription()
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(
            """{"outcome":"done","pullRequest":{"summary":"add retry to webhook delivery","body":"Adds retry."}}"""));

        // Assert
        verdict.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => verdict!.Outcome.ShouldBe(ImplementOutcome.Done),
            () => verdict!.Questions.ShouldBeEmpty(),
            () => verdict!.PullRequest.ShouldNotBeNull().Summary.ShouldNotBeNull().Value.ShouldBe("add retry to webhook delivery"),
            () => verdict!.PullRequest!.Description.ShouldNotBeNull().Value.ShouldBe("Adds retry."));
    }

    [Fact]
    public void WhenThePullRequestBodyCarriesAttribution_TheDescriptionIsScrubbed()
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(
            """{"outcome":"done","pullRequest":{"summary":"add retry","body":"Adds retry.\n\n🤖 Generated with [Claude Code](https://claude.com/claude-code)\n\nCo-Authored-By: Claude <noreply@anthropic.com>"}}"""));

        // Assert
        verdict.ShouldNotBeNull().PullRequest.ShouldNotBeNull().Description.ShouldNotBeNull().Value.ShouldBe("Adds retry.");
    }

    [Theory]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry\nto delivery","body":"Adds retry."}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"","body":"Adds retry."}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","body":"Adds retry."}}""")]
    public void WhenTheSummaryBreaksItsRules_TheVerdictStaysDoneWithoutASummary(string json)
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(json));

        // Assert
        ImplementPullRequest pullRequest = verdict.ShouldNotBeNull().PullRequest.ShouldNotBeNull();
        pullRequest.ShouldSatisfyAllConditions(
            () => pullRequest.Summary.ShouldBeNull(),
            () => pullRequest.Description.ShouldNotBeNull());
    }

    [Fact]
    public void WhenTheBodyIsOnlyAttribution_TheVerdictStaysDoneWithoutADescription()
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(
            """{"outcome":"done","pullRequest":{"summary":"add retry","body":"Co-Authored-By: Claude <noreply@anthropic.com>"}}"""));

        // Assert
        ImplementPullRequest pullRequest = verdict.ShouldNotBeNull().PullRequest.ShouldNotBeNull();
        pullRequest.ShouldSatisfyAllConditions(
            () => pullRequest.Summary.ShouldNotBeNull(),
            () => pullRequest.Description.ShouldBeNull());
    }

    [Fact]
    public void WhenOutcomeIsNeedsInputWithQuestions_ParsesTheQuestions()
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(
            """{"outcome":"needs_input","questions":[{"question":"Which database?","context":"Both are named.","recommendation":"Postgres."},{"question":"Keep v1?","context":"","recommendation":"Yes."}]}"""));

        // Assert
        verdict.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => verdict!.Outcome.ShouldBe(ImplementOutcome.NeedsInput),
            () => verdict!.PullRequest.ShouldBeNull(),
            () => verdict!.Questions.ShouldBe(
            [
                new RunVerdictQuestion("Which database?", "Both are named.", "Postgres."),
                new RunVerdictQuestion("Keep v1?", string.Empty, "Yes."),
            ]));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"outcome":"finished"}""")]
    [InlineData("""{"outcome":1}""")]
    [InlineData("""{"outcome":"done"}""")]
    [InlineData("""{"outcome":"done","pullRequest":null}""")]
    [InlineData("""{"outcome":"done","pullRequest":"add retry"}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry"}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"body":"Adds retry."}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":1,"body":"Adds retry."}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","body":null}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","body":"Adds retry.","title":"x"}}""")]
    [InlineData("""{"outcome":"needs_input"}""")]
    [InlineData("""{"outcome":"needs_input","questions":[]}""")]
    [InlineData("""{"outcome":"needs_input","questions":"Which database?"}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"","context":"x"}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"Which?"}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"Which?","context":"x","extra":1}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":["Which?"]}""")]
    [InlineData("""{"outcome":"done","questions":[],"pullRequest":{"summary":"add retry","body":"Adds retry."}}""")]
    [InlineData("""{"outcome":"done","summary":"extra property","pullRequest":{"summary":"add retry","body":"Adds retry."}}""")]
    public void WhenTheOutputBreaksTheSchema_ReturnsNull(string json)
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }
}
