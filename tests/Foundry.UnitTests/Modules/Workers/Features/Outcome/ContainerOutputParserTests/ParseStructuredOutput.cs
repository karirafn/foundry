using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Features.Outcome;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ContainerOutputParserTests;

public sealed class ParseStructuredOutput
{
    private readonly IContainerOutputParser _sut = new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance);

    [Fact]
    public void WhenTheLastResultEventCarriesStructuredOutput_ReturnsIt()
    {
        // Arrange
        string log = string.Join(
            '\n',
            """{"type":"system","subtype":"init","model":"claude-test"}""",
            """{"type":"assistant","message":{"content":[]}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"Done","structured_output":{"outcome":"done"}}""");

        // Act
        JsonObject? output = _sut.ParseStructuredOutput(log);

        // Assert
        output.ShouldNotBeNull()["outcome"]!.GetValue<string>().ShouldBe("done");
    }

    [Fact]
    public void WhenSeveralResultEventsExist_ReadsOnlyTheLast()
    {
        // Arrange — a transient-error first run, then the claude -c resume.
        string log = string.Join(
            '\n',
            """{"type":"result","is_error":true,"api_error_status":503,"result":"API Error","structured_output":{"outcome":"needs_input"}}""",
            """{"type":"result","is_error":false,"result":"Done","structured_output":{"outcome":"done"}}""");

        // Act
        JsonObject? output = _sut.ParseStructuredOutput(log);

        // Assert
        output.ShouldNotBeNull()["outcome"]!.GetValue<string>().ShouldBe("done");
    }

    [Fact]
    public void WhenTheLastResultEventHasNoStructuredOutput_ReturnsNull()
    {
        // Arrange — the first run's verdict does not survive a resume that never produced one.
        string log = string.Join(
            '\n',
            """{"type":"result","is_error":false,"result":"First","structured_output":{"outcome":"done"}}""",
            """{"type":"result","subtype":"error_max_turns","is_error":true,"result":"Max turns"}""");

        // Act
        JsonObject? output = _sut.ParseStructuredOutput(log);

        // Assert
        output.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"type":"result","result":"Done","structured_output":"done"}""")]
    [InlineData("""{"type":"result","result":"Done","structured_output":null}""")]
    [InlineData("""{"type":"result","result":"Done","structured_output":["done"]}""")]
    public void WhenStructuredOutputIsNotAnObject_ReturnsNull(string resultLine)
    {
        // Act
        JsonObject? output = _sut.ParseStructuredOutput(resultLine);

        // Assert
        output.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=boom")]
    public void WhenThereIsNoResultEvent_ReturnsNull(string? log)
    {
        // Act
        JsonObject? output = _sut.ParseStructuredOutput(log);

        // Assert
        output.ShouldBeNull();
    }

    [Fact]
    public void WhenTheResultLineHasADockerTimestamp_ReturnsTheStructuredOutput()
    {
        // Arrange
        string log = """2026-11-06T12:00:00.123456789Z {"type":"result","result":"Done","structured_output":{"outcome":"done"}}""";

        // Act
        JsonObject? output = _sut.ParseStructuredOutput(log);

        // Assert
        output.ShouldNotBeNull().ContainsKey("outcome").ShouldBeTrue();
    }

    [Fact]
    public void WhenTheResultLineIsLongerThanTheSummaryCap_StillReturnsTheStructuredOutput()
    {
        // Arrange — many questions make the result line longer than the 16 KB result-summary cap.
        JsonArray questions = [];

        for (int i = 0; i < 100; i++)
        {
            questions.Add(new JsonObject { ["question"] = $"Question {i}?", ["context"] = new string('x', 200) });
        }

        JsonObject result = new()
        {
            ["type"] = "result",
            ["result"] = "Need input",
            ["structured_output"] = new JsonObject { ["outcome"] = "needs_input", ["questions"] = questions },
        };

        // Act
        JsonObject? output = _sut.ParseStructuredOutput(result.ToJsonString());

        // Assert
        output.ShouldNotBeNull()["questions"].ShouldBeOfType<JsonArray>().Count.ShouldBe(100);
    }
}
