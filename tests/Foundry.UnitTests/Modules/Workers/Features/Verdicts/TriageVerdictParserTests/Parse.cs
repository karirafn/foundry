using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.TriageVerdictParserTests;

public sealed class Parse
{
    private readonly TriageVerdictParser _sut = new();

    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Role_IsTriage()
    {
        // Assert
        _sut.Role.ShouldBe(WorkerRole.Triage);
    }

    [Fact]
    public void WhenTheIssueIsReady_RecordsADoneVerdict()
    {
        // Act
        RunVerdict verdict = _sut.Parse(Json("""{"outcome":"ready","kind":"feature"}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.Done());
    }

    [Fact]
    public void WhenTheIssueNeedsInput_RecordsItsQuestions()
    {
        // Act
        RunVerdict verdict = _sut.Parse(Json(
            """{"outcome":"needs_input","kind":"feature","questions":[{"question":"Which page?","context":"None is named.","recommendation":"The home page."}]}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.NeedsInput([new RunVerdictQuestion("Which page?", "None is named.", "The home page.")]));
    }

    [Fact]
    public void WhenTheOutputBreaksTheSchema_RecordsAnInvalidVerdict()
    {
        // Act
        RunVerdict verdict = _sut.Parse(Json("""{"outcome":"ready"}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.Invalid());
    }
}
