using Foundry.Modules.Workers.Features.Outcome;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ContainerOutputParserTests;

public sealed class ParseModelUnavailable
{
    private readonly IContainerOutputParser _sut = new ContainerOutputParser(
        NullLogger<ContainerOutputParser>.Instance);

    [Fact]
    public void WhenResultReportsTheSelectedModelError_ReturnsModelUnavailable()
    {
        // Arrange — the CLI validates the model on its first request only
        string log = """
            {"type":"system","subtype":"init","model":"claude-x"}
            {"type":"result","subtype":"success","is_error":true,"api_error_status":404,"duration_ms":900,"num_turns":1,"result":"There's an issue with the selected model (claude-x). It may not exist or you may not have access to it. Run /model to pick a different model."}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.ModelUnavailable>();
    }

    [Fact]
    public void WhenResultUsesATypographicApostrophe_ReturnsModelUnavailable()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"success","is_error":true,"api_error_status":null,"result":"There’s an issue with the selected model (opus). Run /model to pick a different model."}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.ModelUnavailable>();
    }

    [Fact]
    public void WhenASuccessfulSummaryQuotesThePhrase_ReturnsNormalExit()
    {
        // Arrange — is_error false: the agent merely mentions the message
        string log = """
            {"type":"result","subtype":"success","is_error":false,"result":"Handled the \"There's an issue with the selected model\" error message in the parser."}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }
}
