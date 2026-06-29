using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerOutputParserTests;

public sealed class ParseRunResultSummary
{
    private const string SuccessJsonLine =
        """{"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"num_turns":5,"result":"All done.","session_id":"abc","terminal_reason":"stop_reason"}""";

    private const string ErrorJsonLine =
        """{"type":"result","subtype":"error","is_error":true,"duration_ms":300,"num_turns":1,"result":"Usage limit hit.","session_id":"xyz","terminal_reason":"blocking_limit"}""";

    private readonly IContainerOutputParser _sut = new ContainerOutputParser();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenNullOrEmptyInput_ReturnsNull(string? log)
    {
        // Arrange (input via theory parameter)

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenNoJsonLineInLog_ReturnsNull()
    {
        // Arrange
        string log = "Plain text output from the container.";

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenJsonLineExceeds4KB_ReturnsNull()
    {
        // Arrange
        string oversizedValue = new string('x', 4_100);
        string log = $$$"""{"type":"result","subtype":"success","result":"{{{oversizedValue}}}"}""";

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenErrorJsonLine_ReturnsIsErrorTrue()
    {
        // Arrange
        string log = ErrorJsonLine;

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        RunResultSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.IsError.ShouldBeTrue(),
            () => summary.Subtype.ShouldBe("error"),
            () => summary.ResultText.ShouldBe("Usage limit hit."),
            () => summary.NumTurns.ShouldBe(1),
            () => summary.DurationMs.ShouldBe(300));
    }

    [Fact]
    public void WhenJsonHasUsageObject_ParsesInputAndOutputTokens()
    {
        // Arrange
        string log =
            """{"type":"result","subtype":"success","is_error":false,"duration_ms":2000,"num_turns":3,"result":"Done.","usage":{"input_tokens":500,"output_tokens":250}}""";

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        RunResultSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.InputTokens.ShouldBe(500),
            () => summary.OutputTokens.ShouldBe(250));
    }

    [Fact]
    public void WhenJsonHasTotalCostUsd_ParsesCost()
    {
        // Arrange
        string log =
            """{"type":"result","subtype":"success","is_error":false,"duration_ms":1000,"num_turns":2,"result":"Done.","total_cost_usd":0.0042}""";

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        RunResultSummary summary = result.ShouldNotBeNull();
        summary.TotalCostUsd.ShouldBe(0.0042m);
    }

    [Fact]
    public void WhenOptionalFieldsAbsent_NullableFieldsAreNull()
    {
        // Arrange — minimal JSON with only required fields
        string log =
            """{"type":"result","subtype":"success","is_error":false,"duration_ms":500,"num_turns":1,"result":"Minimal."}""";

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        RunResultSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.TotalCostUsd.ShouldBeNull(),
            () => summary.InputTokens.ShouldBeNull(),
            () => summary.OutputTokens.ShouldBeNull());
    }

    [Fact]
    public void WhenLogExceeds64KB_TruncatesAndStillParsesLastJsonLine()
    {
        // Arrange
        string padding = new string('x', 70_000) + "\n";
        string log = padding + SuccessJsonLine;

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        RunResultSummary summary = result.ShouldNotBeNull();
        summary.ResultText.ShouldBe("All done.");
    }

    [Fact]
    public void WhenMalformedJson_ReturnsNull()
    {
        // Arrange
        string log = "{not valid json at all";

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenSuccessJsonLine_ReturnsPopulatedSummary()
    {
        // Arrange
        string log = SuccessJsonLine;

        // Act
        RunResultSummary? result = _sut.ParseRunResultSummary(log);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldSatisfyAllConditions(
            () => result.ResultText.ShouldBe("All done."),
            () => result.Subtype.ShouldBe("success"),
            () => result.IsError.ShouldBeFalse(),
            () => result.DurationMs.ShouldBe(1234),
            () => result.NumTurns.ShouldBe(5));
    }
}
