using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Verdicts;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.RunVerdictReaderTests;

public sealed class Read
{
    private readonly RunVerdictReader _sut = new(
        new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance),
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
            Log("""{"type":"result","result":"Done","structured_output":{"outcome":"done"}}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.Done());
    }

    [Fact]
    public void WhenTheImplementVerdictNeedsInput_ReturnsItsQuestions()
    {
        // Act
        RunVerdict? verdict = _sut.Read(
            WorkerRole.Implement,
            Log("""{"type":"result","result":"Blocked","structured_output":{"outcome":"needs_input","questions":[{"question":"Which?","context":"Two options."}]}}"""));

        // Assert
        verdict.ShouldBe(RunVerdict.NeedsInput([new RunVerdictQuestion("Which?", "Two options.")]));
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
