using Foundry.Modules.Workers.Features.Outcome;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ContainerOutputParserTests;

public sealed class ParseTurnLimit
{
    private readonly IContainerOutputParser _sut = new ContainerOutputParser(
        NullLogger<ContainerOutputParser>.Instance);

    [Fact]
    public void WhenResultSubtypeIsErrorMaxTurns_ReturnsTurnLimitReached()
    {
        // Arrange — pins the CLI's current subtype string for an exhausted --max-turns budget
        string log = """
            {"type":"system","subtype":"init","model":"claude-x"}
            {"type":"result","subtype":"error_max_turns","is_error":true,"duration_ms":3600000,"num_turns":301,"session_id":"abc"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TurnLimitReached>();
    }

    [Fact]
    public void WhenAResumeHitsTheLimitAfterATransientError_ReturnsTurnLimitReached()
    {
        // Arrange — the resume's result line is the last one in the log
        string log = """
            {"type":"result","subtype":"success","is_error":true,"api_error_status":529,"result":"API Error: 529 Overloaded"}
            {"type":"result","subtype":"error_max_turns","is_error":true,"num_turns":301}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TurnLimitReached>();
    }

    [Fact]
    public void WhenErrorMaxTurnsAlsoCarriesAUsageLimit_ReturnsUsageLimited()
    {
        // Arrange — the usage limit is the cause; the turn limit is only the cutoff
        string log = """
            {"type":"result","subtype":"error_max_turns","is_error":true,"api_error_status":429,"result":"Usage limit reached. Resets at 2026-06-18T15:00:00Z."}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
    }

    [Fact]
    public void WhenErrorMaxTurnsAlsoCarriesCreditsExhausted_ReturnsCreditsExhausted()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"error_max_turns","is_error":true,"terminal_reason":"blocking_limit","result":"Credit balance too low"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
    }

    [Fact]
    public void WhenErrorMaxTurnsAlsoCarriesAnAuthFailure_ReturnsAuthInvalid()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"error_max_turns","is_error":true,"api_error_status":401}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.AuthInvalid>();
    }

    [Fact]
    public void WhenErrorMaxTurnsAlsoCarriesATransientApiError_ReturnsTransientApiError()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"error_max_turns","is_error":true,"api_error_status":529}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TransientApiError>();
    }

    [Fact]
    public void WhenErrorMaxTurnsAlsoCarriesModelUnavailable_ReturnsModelUnavailable()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"error_max_turns","is_error":true,"result":"There's an issue with the selected model (claude-x)."}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.ModelUnavailable>();
    }
}
