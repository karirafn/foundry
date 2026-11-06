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
    public void WhenOutcomeIsDone_ParsesADoneVerdict()
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json("""{"outcome":"done"}"""));

        // Assert
        verdict.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => verdict!.Outcome.ShouldBe(ImplementOutcome.Done),
            () => verdict!.Questions.ShouldBeEmpty());
    }

    [Fact]
    public void WhenOutcomeIsNeedsInputWithQuestions_ParsesTheQuestions()
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(
            """{"outcome":"needs_input","questions":[{"question":"Which database?","context":"Both are named."},{"question":"Keep v1?","context":""}]}"""));

        // Assert
        verdict.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => verdict!.Outcome.ShouldBe(ImplementOutcome.NeedsInput),
            () => verdict!.Questions.ShouldBe(
            [
                new RunVerdictQuestion("Which database?", "Both are named."),
                new RunVerdictQuestion("Keep v1?", string.Empty),
            ]));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"outcome":"finished"}""")]
    [InlineData("""{"outcome":1}""")]
    [InlineData("""{"outcome":"needs_input"}""")]
    [InlineData("""{"outcome":"needs_input","questions":[]}""")]
    [InlineData("""{"outcome":"needs_input","questions":"Which database?"}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"","context":"x"}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"Which?"}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":[{"question":"Which?","context":"x","extra":1}]}""")]
    [InlineData("""{"outcome":"needs_input","questions":["Which?"]}""")]
    [InlineData("""{"outcome":"done","questions":[]}""")]
    [InlineData("""{"outcome":"done","summary":"extra property"}""")]
    public void WhenTheOutputBreaksTheSchema_ReturnsNull(string json)
    {
        // Act
        ImplementVerdict? verdict = ImplementVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }
}
