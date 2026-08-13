using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Outcome;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ProbeOutcomeClassifierTests;

public sealed class Classify
{
    private readonly IProbeOutcomeClassifier _sut = new ProbeOutcomeClassifier(
        new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance));

    [Fact]
    public void WhenLogsProduceNormalExit_ReturnsAvailable()
    {
        // Arrange
        string logs = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"num_turns":5,"result":"All done.","session_id":"abc","terminal_reason":"stop_reason"}
            """;

        // Act
        ProbeOutcome result = _sut.Classify(logs);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.Available>();
    }

    [Fact]
    public void WhenLogsProduceCreditsExhausted_ReturnsCreditsStillBlocked()
    {
        // Arrange
        string logs = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":200,"num_turns":1,"result":"Usage limit exceeded. No timestamp available.","session_id":"def","terminal_reason":"blocking_limit"}
            """;

        // Act
        ProbeOutcome result = _sut.Classify(logs);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.CreditsStillBlocked>();
    }

    [Fact]
    public void WhenLogsProduceUsageLimited_ReturnsUsageLimitedWithResetsAt()
    {
        // Arrange
        string logs = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. Resets at 2026-06-18T15:00:00Z.","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ProbeOutcome result = _sut.Classify(logs);

        // Assert
        ProbeOutcome.UsageLimited limited = result.ShouldBeOfType<ProbeOutcome.UsageLimited>();
        limited.ResetsAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WhenLogsAreNull_ReturnsInfrastructureFailure()
    {
        // Arrange / Act
        ProbeOutcome result = _sut.Classify(null);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.InfrastructureFailure>();
    }

    [Fact]
    public void WhenLogsAreEmpty_ReturnsInfrastructureFailure()
    {
        // Arrange / Act
        ProbeOutcome result = _sut.Classify(string.Empty);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.InfrastructureFailure>();
    }

    [Fact]
    public void WhenLogsProduceAuthInvalid_ReturnsInfrastructureFailure()
    {
        // Arrange
        string logs = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"Auth failed.","session_id":"abc","api_error_status":401}
            """;

        // Act
        ProbeOutcome result = _sut.Classify(logs);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.InfrastructureFailure>();
    }

    [Fact]
    public void WhenLogsProduceTransientApiError_ReturnsInfrastructureFailure()
    {
        // Arrange
        string logs = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"Server error","session_id":"abc","api_error_status":503}
            """;

        // Act
        ProbeOutcome result = _sut.Classify(logs);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.InfrastructureFailure>();
    }

    [Fact]
    public void WhenLogsProduceParseFailure_ReturnsInfrastructureFailure()
    {
        // Arrange — produce a parse failure: a JSON line exceeding MaxJsonLineLength (4 KB)
        string longJson = "{\"result\":\"" + new string('x', 5000) + "\"}";

        // Act
        ProbeOutcome result = _sut.Classify(longJson);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.InfrastructureFailure>();
    }

    [Fact]
    public void WhenLogsProduceNoResultLine_ReturnsInfrastructureFailure()
    {
        // Arrange — plain text with no JSON line
        string logs = "plain text output with no JSON";

        // Act
        ProbeOutcome result = _sut.Classify(logs);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.InfrastructureFailure>();
    }

    [Fact]
    public void WhenLogsProduceWorkerBootstrapFailed_ReturnsInfrastructureFailure()
    {
        // Arrange — bootstrap sentinel triggers WorkerBootstrapFailed
        string logs = "FOUNDRY_BOOTSTRAP_FAILED stage=clone failed to clone repo";

        // Act
        ProbeOutcome result = _sut.Classify(logs);

        // Assert
        result.ShouldBeOfType<ProbeOutcome.InfrastructureFailure>();
    }
}
