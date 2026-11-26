using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Testing;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.RunVerdictReaderTests;

public sealed class Read
{
    private readonly RunVerdictReader _sut = new(
        new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance, new PlanUsageWindowReader()),
        [new ImplementVerdictParser()]);

    private static string Log(string resultLine) => string.Join(
        '\n',
        """{"type":"system","subtype":"init","model":"claude-test"}""",
        resultLine);

    [Fact]
    public void WhenTheImplementVerdictIsDone_ReturnsDone()
    {
        // Act
        RunVerdict? verdict = _sut.Read(
            WorkerRole.Implement,
            Log("""{"type":"result","result":"Done","structured_output":{"outcome":"done","pullRequest":{"summary":"add retry","changes":["Adds retry."],"verification":["dotnet test: passed."]}}}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.Done());
    }

    [Fact]
    public void WhenTheImplementVerdictNeedsInput_ReturnsItsQuestions()
    {
        // Act
        RunVerdict? verdict = _sut.Read(
            WorkerRole.Implement,
            Log("""{"type":"result","result":"Blocked","structured_output":{"outcome":"needs_input","questions":[{"question":"Which?","problem":"Two are configured.","impact":"Migrations target the wrong one.","options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},"evidence":["src/Db.cs:12 opens SQLite"]}]}}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.NeedsInput([StructuredQuestionBuilder.Build(question: "Which?")]));
    }

    [Fact]
    public void WhenTheResultEventCarriesNoStructuredOutput_ReturnsMissing()
    {
        // Act
        RunVerdict? verdict = _sut.Read(
            WorkerRole.Implement,
            Log("""{"type":"result","subtype":"error_max_turns","is_error":true,"result":"Max turns"}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.Missing());
    }

    [Fact]
    public void WhenThereIsNoLog_ReturnsMissing()
    {
        // Act
        RunVerdict? verdict = _sut.Read(WorkerRole.Implement, null);

        // Assert
        verdict.ShouldBe(RunVerdict.Missing());
    }

    [Fact]
    public void WhenTheStructuredOutputFailsTheTypedParse_ReturnsInvalid()
    {
        // Act
        RunVerdict? verdict = _sut.Read(
            WorkerRole.Implement,
            Log("""{"type":"result","result":"Blocked","structured_output":{"outcome":"needs_input"}}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.Invalid());
    }

    [Fact]
    public void WhenTheRoleHasNoVerdictParser_ReturnsNull()
    {
        // Act
        RunVerdict? verdict = _sut.Read(
            WorkerRole.FromStored("retired-role"),
            Log("""{"type":"result","result":"Done","structured_output":{"outcome":"done"}}"""));

        // Assert
        verdict.ShouldBeNull();
    }
}
