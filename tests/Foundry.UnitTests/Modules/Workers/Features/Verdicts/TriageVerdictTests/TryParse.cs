using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.TriageVerdictTests;

public sealed class TryParse
{
    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    [Theory]
    [InlineData("feature", nameof(TriageIssueKind.Feature))]
    [InlineData("bug", nameof(TriageIssueKind.Bug))]
    [InlineData("refactor", nameof(TriageIssueKind.Refactor))]
    [InlineData("documentation", nameof(TriageIssueKind.Documentation))]
    public void WhenOutcomeIsReady_ParsesAReadyVerdictWithItsKind(string kindToken, string expected)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json($$"""{"outcome":"ready","kind":"{{kindToken}}"}"""));

        // Assert
        verdict.ShouldBe(new TriageVerdict.Ready(Enum.Parse<TriageIssueKind>(expected)));
    }

    [Fact]
    public void WhenOutcomeIsNeedsInputWithQuestions_ParsesTheKindAndQuestions()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            """{"outcome":"needs_input","kind":"bug","questions":[{"question":"Which endpoint?","context":"Two match.","recommendation":"The v2 one."}]}"""));

        // Assert
        TriageVerdict.NeedsInput needsInput = verdict.ShouldBeOfType<TriageVerdict.NeedsInput>();
        needsInput.ShouldSatisfyAllConditions(
            () => needsInput.Kind.ShouldBe(TriageIssueKind.Bug),
            () => needsInput.Questions.ShouldBe([new RunVerdictQuestion("Which endpoint?", "Two match.", "The v2 one.")]));
    }

    [Fact]
    public void WhenOutcomeIsBlockedWithBlockers_ParsesTheKindAndBlockers()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            """{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs the auth module."},{"issue":16,"reason":"Needs the schema."}]}"""));

        // Assert
        verdict.ShouldBe(new TriageVerdict.Blocked(
            TriageIssueKind.Feature,
            [new TriageBlocker(15, "Needs the auth module."), new TriageBlocker(16, "Needs the schema.")]));
    }

    [Theory]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"   "}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"\n\n"}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"\t\u0007 \r\n"}]}""")]
    public void WhenABlockerReasonIsBlankAfterNormalization_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("Needs the auth\\n\\nmodule.", "Needs the auth module.")]
    [InlineData("  Needs\\tthe   auth module.\\r\\n", "Needs the auth module.")]
    [InlineData("Needs the\\u0007 auth\\u001b module.", "Needs the auth module.")]
    public void WhenABlockerReasonSpansLinesOrHoldsControlCharacters_NormalizesItToOneLine(string rawReason, string expected)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            $$"""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"{{rawReason}}"}]}"""));

        // Assert
        verdict.ShouldBe(new TriageVerdict.Blocked(TriageIssueKind.Feature, [new TriageBlocker(15, expected)]));
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"feature","blockedBy":[{"issue":15,"reason":"x"}]}""", nameof(TriageVerdict.Ready))]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"Q?","context":"c","recommendation":"r"}],"blockedBy":[{"issue":15,"reason":"x"}]}""", nameof(TriageVerdict.NeedsInput))]
    public void WhenAWellFormedBlockedByAccompaniesAnotherOutcome_ItIsIgnored(string json, string expectedType)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldNotBeNull().GetType().Name.ShouldBe(expectedType);
    }

    [Theory]
    [InlineData("""{"outcome":"blocked","kind":"feature"}""")]

    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":null}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":{"issue":15,"reason":"x"}}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[15]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":["x"]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":"15","reason":"x"}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15.5,"reason":"x"}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":null,"reason":"x"}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":""}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":7}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"reason":"x"}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"x","extra":1}]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"x"},{"issue":16}]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","blockedBy":[]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","blockedBy":[{"issue":15}]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"Q?","context":"c","recommendation":"r"}],"blockedBy":"x"}""")]
    public void WhenTheBlockedVerdictBreaksTheSchema_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"outcome":"ready"}""")]
    [InlineData("""{"kind":"feature"}""")]
    [InlineData("""{"outcome":"done","kind":"feature"}""")]
    [InlineData("""{"outcome":"too_big","kind":"feature"}""")]
    [InlineData("""{"outcome":"ready","kind":"chore"}""")]
    [InlineData("""{"outcome":"ready","kind":"Feature"}""")]
    [InlineData("""{"outcome":"ready","kind":1}""")]
    [InlineData("""{"outcome":1,"kind":"feature"}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature"}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"","context":"x"}]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"Which?"}]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","questions":[]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","summary":"extra property"}""")]
    public void WhenTheOutputBreaksTheSchema_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }
}
