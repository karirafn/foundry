using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.TriageVerdictTests;

public sealed class TryParseDecisions
{
    private const string Decision =
        """{"question":"Which retry cap?","decision":"Three attempts.","ground":"settled_business","evidence":["src/Retry.cs:8 caps at three"]}""";

    private const string Question =
        """{"question":"Which endpoint?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}""";

    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    private static TriageDecision Expected(string question, string decision, TriageDecisionGround ground, params string[] evidence) =>
        TriageDecision.Create(question, decision, ground, evidence).ShouldBeOfType<Result<TriageDecision>.Success>().Value;

    [Fact]
    public void WhenAReadyVerdictCarriesDecisions_ParsesEachDecision()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            $$"""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"none","decisions":[{{Decision}},{"question":"Where does the setting live?","decision":"In appsettings.json under Retry.","ground":"implementation_detail","evidence":["src/Options.cs:3 binds Retry","appsettings.json:9 holds it"]}]}"""));

        // Assert
        verdict.ShouldBeOfType<TriageVerdict.Ready>().Decisions.ShouldBe(
        [
            Expected("Which retry cap?", "Three attempts.", TriageDecisionGround.SettledBusiness, "src/Retry.cs:8 caps at three"),
            Expected(
                "Where does the setting live?",
                "In appsettings.json under Retry.",
                TriageDecisionGround.ImplementationDetail,
                "src/Options.cs:3 binds Retry",
                "appsettings.json:9 holds it"),
        ]);
    }

    [Fact]
    public void WhenANeedsInputVerdictCarriesDecisions_ParsesThemBesideTheQuestions()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            $$"""{"outcome":"needs_input","kind":"bug","questions":[{{Question}}],"decisions":[{{Decision}}]}"""));

        // Assert
        TriageVerdict.NeedsInput needsInput = verdict.ShouldBeOfType<TriageVerdict.NeedsInput>();
        needsInput.ShouldSatisfyAllConditions(
            () => needsInput.Questions.ShouldHaveSingleItem().Question.ShouldBe("Which endpoint?"),
            () => needsInput.Decisions.ShouldBe(
                [Expected("Which retry cap?", "Three attempts.", TriageDecisionGround.SettledBusiness, "src/Retry.cs:8 caps at three")]));
    }

    [Fact]
    public void WhenAVerdictCarriesNoDecisions_ItsDecisionsAreEmpty()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json("""{"outcome":"ready","kind":"bug","demand":"routine","demandReason":"none"}"""));

        // Assert
        verdict.ShouldBeOfType<TriageVerdict.Ready>().Decisions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs it."}],"decisions":[]}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs it."}],"decisions":[{"question":"Which retry cap?","decision":"Three attempts.","ground":"settled_business","evidence":["src/Retry.cs:8 caps at three"]}]}""")]
    public void WhenABlockedVerdictCarriesDecisions_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"question":"  ","decision":"Three attempts.","ground":"settled_business","evidence":["src/Retry.cs:8"]}""")]
    [InlineData("""{"question":"Which retry cap?","decision":"","ground":"settled_business","evidence":["src/Retry.cs:8"]}""")]
    [InlineData("""{"question":"Which retry cap?","decision":"Three attempts.","ground":"guessed","evidence":["src/Retry.cs:8"]}""")]
    [InlineData("""{"question":"Which retry cap?","decision":"Three attempts.","ground":"settled_business","evidence":[]}""")]
    [InlineData("""{"question":"Which retry cap?","decision":"Three attempts.","ground":"settled_business","evidence":["  "]}""")]
    [InlineData("""{"question":"Which retry cap?","decision":"Three attempts.","ground":"settled_business"}""")]
    [InlineData("""{"question":"Which retry cap?","decision":"Three attempts.","ground":"settled_business","evidence":["a","b","c","d","e","f"]}""")]
    [InlineData("""{"question":"Which\nretry cap?","decision":"Three attempts.","ground":"settled_business","evidence":["src/Retry.cs:8"]}""")]
    [InlineData("""{"question":"Which retry cap?","decision":"Three attempts.","ground":"settled_business","evidence":["src/Retry.cs:8"],"extra":1}""")]
    [InlineData("""{"question":"Which retry cap?","decision":3,"ground":"settled_business","evidence":["src/Retry.cs:8"]}""")]
    public void WhenADecisionBreaksTheSchema_ReturnsNull(string decision)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json($$"""{"outcome":"ready","kind":"bug","demand":"routine","demandReason":"none","decisions":[{{decision}}]}"""));

        // Assert
        verdict.ShouldBeNull();
    }

    [Fact]
    public void WhenADecisionIsLongerThanTheSchemaAllows_ReturnsNull()
    {
        // Arrange
        string decision = new('x', TriageDecision.DecisionMaxLength + 1);

        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            $$"""{"outcome":"ready","kind":"bug","demand":"routine","demandReason":"none","decisions":[{"question":"Which?","decision":"{{decision}}","ground":"settled_business","evidence":["src/Retry.cs:8"]}]}"""));

        // Assert
        verdict.ShouldBeNull();
    }

    [Fact]
    public void WhenAVerdictCarriesMoreDecisionsThanTheCap_ReturnsNull()
    {
        // Arrange
        string decisions = string.Join(",", Enumerable.Repeat(Decision, TriageResult.MaxDecisions + 1));

        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json($$"""{"outcome":"ready","kind":"bug","demand":"routine","demandReason":"none","decisions":[{{decisions}}]}"""));

        // Assert
        verdict.ShouldBeNull();
    }

    [Fact]
    public void WhenDecisionsIsNotAnArray_ReturnsNull()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json($$"""{"outcome":"ready","kind":"bug","demand":"routine","demandReason":"none","decisions":{{Decision}}}"""));

        // Assert
        verdict.ShouldBeNull();
    }
}
