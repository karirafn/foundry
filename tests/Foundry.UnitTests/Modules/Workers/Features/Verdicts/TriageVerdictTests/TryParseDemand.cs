using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.TriageVerdictTests;

public sealed class TryParseDemand
{
    private const string Question =
        """{"question":"Which endpoint?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}""";

    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    private static JsonObject Ready(JsonNode? demand, JsonNode? demandReason)
    {
        JsonObject output = new() { ["outcome"] = "ready", ["kind"] = "feature" };

        if (demand is not null)
        {
            output["demand"] = demand;
        }

        if (demandReason is not null)
        {
            output["demandReason"] = demandReason;
        }

        return output;
    }

    [Fact]
    public void WhenAReadyVerdictIsRoutineWithAReason_ParsesTheDemandAndReason()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Ready("routine", "none"));

        // Assert
        TriageVerdict.Ready ready = verdict.ShouldBeOfType<TriageVerdict.Ready>();
        ready.ShouldSatisfyAllConditions(
            () => ready.Demand.ShouldBe(ImplementDemand.Routine),
            () => ready.DemandReason.ShouldBe("none"));
    }

    [Fact]
    public void WhenAReadyVerdictIsDemandingWithTheReasonNone_ParsesIt()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Ready("demanding", "none"));

        // Assert
        TriageVerdict.Ready ready = verdict.ShouldBeOfType<TriageVerdict.Ready>();
        ready.ShouldSatisfyAllConditions(
            () => ready.Demand.ShouldBe(ImplementDemand.Demanding),
            () => ready.DemandReason.ShouldBe("none"));
    }

    [Fact]
    public void WhenAReadyVerdictIsRoutineWithAReasonNamingATrigger_ParsesItWithoutCrossChecking()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Ready("routine", "Adds a persistence schema column."));

        // Assert
        TriageVerdict.Ready ready = verdict.ShouldBeOfType<TriageVerdict.Ready>();
        ready.ShouldSatisfyAllConditions(
            () => ready.Demand.ShouldBe(ImplementDemand.Routine),
            () => ready.DemandReason.ShouldBe("Adds a persistence schema column."));
    }

    [Fact]
    public void WhenTheReasonIsExactlyTheMaximumLength_ParsesIt()
    {
        // Arrange
        string reason = new('a', TriageResult.Ready.DemandReasonMaxLength);

        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Ready("demanding", reason));

        // Assert
        verdict.ShouldBeOfType<TriageVerdict.Ready>().DemandReason.ShouldBe(reason);
    }

    [Fact]
    public void WhenTheReasonIsOverTheMaximumLength_ReturnsNull()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Ready("demanding", new string('a', TriageResult.Ready.DemandReasonMaxLength + 1)));

        // Assert
        verdict.ShouldBeNull();
    }

    [Fact]
    public void WhenAReadyVerdictHasNoDemand_ReturnsNull()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Ready(demand: null, demandReason: "none"));

        // Assert
        verdict.ShouldBeNull();
    }

    [Fact]
    public void WhenAReadyVerdictHasNoDemandReason_ReturnsNull()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Ready("routine", demandReason: null));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"hard","demandReason":"none"}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"Routine","demandReason":"none"}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"opus","demandReason":"none"}""")]
    public void WhenTheDemandTokenIsUnknown_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":1,"demandReason":"none"}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":null,"demandReason":"none"}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":["routine"],"demandReason":"none"}""")]
    public void WhenTheDemandIsNotAString_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"Adds a\ncolumn."}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"Adds a column.\n"}""")]
    public void WhenTheReasonHoldsALineFeed_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"Adds a\rcolumn."}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"\rAdds a column."}""")]
    public void WhenTheReasonHoldsACarriageReturn_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":""}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"   "}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"\t"}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":7}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":null}""")]
    public void WhenTheReasonIsEmptyBlankOrNotAString_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData($$"""{"outcome":"needs_input","kind":"feature","questions":[{{Question}}],"demand":"routine"}""")]
    [InlineData($$"""{"outcome":"needs_input","kind":"feature","questions":[{{Question}}],"demandReason":"none"}""")]
    [InlineData($$"""{"outcome":"needs_input","kind":"feature","questions":[{{Question}}],"demand":"routine","demandReason":"none"}""")]
    public void WhenANeedsInputVerdictCarriesADemandOrReason_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs the schema."}],"demand":"demanding"}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs the schema."}],"demandReason":"none"}""")]
    [InlineData("""{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs the schema."}],"demand":"demanding","demandReason":"none"}""")]
    public void WhenABlockedVerdictCarriesADemandOrReason_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }
}
