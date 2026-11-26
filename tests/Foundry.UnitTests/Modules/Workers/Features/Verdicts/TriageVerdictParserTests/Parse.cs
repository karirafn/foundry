using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Testing;

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
        RunVerdict verdict = _sut.Parse(Json("""{"outcome":"ready","kind":"feature","demand":"routine","demandReason":"none"}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.Done());
    }

    [Fact]
    public void WhenTheIssueNeedsInput_RecordsItsQuestions()
    {
        // Act
        RunVerdict verdict = _sut.Parse(Json(
            """{"outcome":"needs_input","kind":"feature","questions":[{"question":"Which page?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.NeedsInput([StructuredQuestionBuilder.Build(question: "Which page?")]));
    }

    [Fact]
    public void WhenTheIssueIsBlocked_RecordsADoneVerdict()
    {
        // Act
        RunVerdict verdict = _sut.Parse(Json(
            """{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs the auth module."}]}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.Done());
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
