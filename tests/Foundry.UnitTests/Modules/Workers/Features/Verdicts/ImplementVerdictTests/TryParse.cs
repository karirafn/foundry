using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Testing;

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
            """{"outcome":"done","pullRequest":{"summary":"add retry to webhook delivery","cause":"The first attempt timed out.","changes":["Adds retry."],"verification":["dotnet test: 3 passed."],"attention":["Not run against staging."],"notes":"No migration."}}"""));

        // Assert
        PullRequestDescription description = verdict.ShouldNotBeNull().PullRequest.ShouldNotBeNull().Description.ShouldNotBeNull();
        verdict.ShouldSatisfyAllConditions(
            () => verdict!.Outcome.ShouldBe(ImplementOutcome.Done),
            () => verdict!.Questions.ShouldBeEmpty(),
            () => verdict!.PullRequest!.Summary.ShouldNotBeNull().Value.ShouldBe("add retry to webhook delivery"),
            () => description.Cause.ShouldBe("The first attempt timed out."),
            () => description.Changes.ShouldBe(["Adds retry."]),
            () => description.Verification.ShouldBe(["dotnet test: 3 passed."]),
            () => description.Attention.ShouldBe(["Not run against staging."]),
            () => description.Notes.ShouldBe("No migration."));
    }

    [Fact]
    public void WhenTheOptionalFieldsAreAbsent_TheDescriptionHasNoCauseAttentionOrNotes()
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(
            """{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."]}}"""));

        // Assert
        PullRequestDescription description = verdict.ShouldNotBeNull().PullRequest.ShouldNotBeNull().Description.ShouldNotBeNull();
        description.ShouldSatisfyAllConditions(
            () => description.Cause.ShouldBeNull(),
            () => description.Attention.ShouldBeEmpty(),
            () => description.Notes.ShouldBeNull());
    }

    [Fact]
    public void WhenTheNotesCarryAttribution_TheDescriptionIsScrubbed()
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(
            """{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."],"notes":"Adds retry.\n\n🤖 Generated with [Claude Code](https://claude.com/claude-code)\n\nCo-Authored-By: Claude <noreply@anthropic.com>"}}"""));

        // Assert
        verdict.ShouldNotBeNull().PullRequest.ShouldNotBeNull().Description.ShouldNotBeNull().Notes.ShouldBe("Adds retry.");
    }

    [Theory]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry\nto delivery","changes":["Adds retry."],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"","changes":["Adds retry."],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","changes":["Adds retry."],"verification":["Ran it."]}}""")]
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

    [Theory]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Co-Authored-By: Claude <noreply@anthropic.com>"],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":[],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds\nretry."],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."],"attention":["a","b","c","d"]}}""")]
    public void WhenTheDescriptionBreaksItsRules_TheVerdictStaysDoneWithoutADescription(string json)
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(json));

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
            """{"outcome":"needs_input","questions":[{"question":"Which database?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]},{"question":"Keep v1?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}"""));

        // Assert
        verdict.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => verdict!.Outcome.ShouldBe(ImplementOutcome.NeedsInput),
            () => verdict!.PullRequest.ShouldBeNull(),
            () => verdict!.Questions.ShouldBe(
            [
                StructuredQuestionBuilder.Build(question: "Which database?"),
                StructuredQuestionBuilder.Build(question: "Keep v1?"),
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
    [InlineData("""{"outcome":"done","pullRequest":{"changes":["Adds retry."],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":1,"changes":["Adds retry."],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":"Adds retry.","verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":[1],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."],"cause":null}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."],"attention":"Check it."}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."],"notes":1}}""")]
    [InlineData("""{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."],"body":"Adds retry."}}""")]
    [InlineData("""{"outcome":"needs_input"}""")]
    [InlineData("""{"outcome":"needs_input","questions":[]}""")]
    [InlineData("""{"outcome":"needs_input","questions":"Which database?"}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"Which?"}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"Which?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"],"extra":1}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":["Which?"]}""")]
    [InlineData("""{"outcome":"done","questions":[],"pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."]}}""")]
    [InlineData("""{"outcome":"done","summary":"extra property","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["Ran it."]}}""")]
    public void WhenTheOutputBreaksTheSchema_ReturnsNull(string json)
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }
}
