using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Testing;

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
        TriageVerdict? verdict = TriageVerdict.TryParse(Json($$"""{"outcome":"ready","kind":"{{kindToken}}","demand":"routine","demandReason":"none"}"""));

        // Assert
        verdict.ShouldBe(new TriageVerdict.Ready(Enum.Parse<TriageIssueKind>(expected), [], ImplementDemand.Routine, "none"));
    }

    [Fact]
    public void WhenOutcomeIsNeedsInputWithQuestions_ParsesTheKindAndQuestions()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            """{"outcome":"needs_input","kind":"bug","questions":[{"question":"Which endpoint?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}"""));

        // Assert
        TriageVerdict.NeedsInput needsInput = verdict.ShouldBeOfType<TriageVerdict.NeedsInput>();
        needsInput.ShouldSatisfyAllConditions(
            () => needsInput.Kind.ShouldBe(TriageIssueKind.Bug),
            () => needsInput.Questions.ShouldBe([StructuredQuestionBuilder.Build(question: "Which endpoint?")]));
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
    [InlineData("\\u202e")]
    [InlineData("\\u200b\\u200d\\ufeff")]
    [InlineData("\\u2066\\u2069 \\u00ad")]
    [InlineData("\\udb40\\udc41\\udb40\\udc7f")]
    public void WhenABlockerReasonHoldsOnlyFormatCharacters_ReturnsNull(string rawReason)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            $$"""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"{{rawReason}}"}]}"""));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("Needs the \\u202eauth module.", "Needs the auth module.")]
    [InlineData("Needs\\u2066 the\\u200b auth\\u2069 module.", "Needs the auth module.")]
    [InlineData("Needs the auth\\udb40\\udc41 module.", "Needs the auth module.")]
    [InlineData("Needs the auth \\ud83d\\ude00 module.", "Needs the auth \U0001F600 module.")]
    public void WhenABlockerReasonHoldsFormatCharacters_RemovesThemAndKeepsOtherText(string rawReason, string expected)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            $$"""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"{{rawReason}}"}]}"""));

        // Assert
        verdict.ShouldBe(new TriageVerdict.Blocked(TriageIssueKind.Feature, [new TriageBlocker(15, expected)]));
    }

    [Theory]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs \ud800 the module."}]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"bug","questions":[{"question":"Which \udc00 endpoint?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}""")]
    public void WhenAStringHoldsALoneSurrogate_ReturnsNullWithoutThrowing(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"none","blockedBy":[{"issue":15,"reason":"x"}]}""", nameof(TriageVerdict.Ready))]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"Q?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}],"blockedBy":[{"issue":15,"reason":"x"}]}""", nameof(TriageVerdict.NeedsInput))]
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
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"none","blockedBy":[]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"none","blockedBy":[{"issue":15}]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"Q?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}],"blockedBy":"x"}""")]
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
    [InlineData("""{"outcome":"ready","kind":"chore","demand":"routine","demandReason":"none"}""")]
    [InlineData("""{"outcome":"ready","kind":"Feature","demand":"routine","demandReason":"none"}""")]
    [InlineData("""{"outcome":"ready","kind":1}""")]
    [InlineData("""{"outcome":1,"kind":"feature"}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature"}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"Which?"}]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"none","questions":[]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"none","summary":"extra property"}""")]
    public void WhenTheOutputBreaksTheSchema_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }
}
