using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerOutputParserTests;

public sealed class Parse
{
    private const int DefaultCooldownMinutes = 60;

    private readonly IContainerOutputParser _sut = new ContainerOutputParser();

    [Fact]
    public void WhenNormalExitJson_ReturnsNormalExit()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"num_turns":5,"result":"All done.","session_id":"abc","terminal_reason":"stop_reason"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenBlockingLimitWithParseableResetTime_ReturnsUsageLimitedWithParsedTime()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. Resets at 2026-06-18T15:00:00Z.","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WhenRapidRefillBreakerWithParseableResetTime_ReturnsUsageLimited()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":300,"num_turns":1,"result":"Rate limit hit. Try again after 2026-06-18T16:30:00+00:00.","session_id":"abc","terminal_reason":"rapid_refill_breaker"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 16, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WhenBlockingLimitWithUnparseableResetTime_ReturnsUsageLimitedWithFallback()
    {
        // Arrange
        DateTimeOffset before = DateTimeOffset.UtcNow;
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":200,"num_turns":1,"result":"Usage limit exceeded. No timestamp available.","session_id":"def","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        DateTimeOffset expectedMin = before.AddMinutes(DefaultCooldownMinutes);
        DateTimeOffset expectedMax = DateTimeOffset.UtcNow.AddMinutes(DefaultCooldownMinutes);
        limited.ResetsAt.ShouldBeInRange(expectedMin, expectedMax);
    }

    [Fact]
    public void WhenNonJsonInput_ReturnsParseFailure()
    {
        // Arrange
        string log = "Plain text output from the container.";

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.ParseFailure failure = result.ShouldBeOfType<ContainerOutputParseResult.ParseFailure>();
        failure.RawOutput.ShouldBe(log);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenNullOrEmptyInput_ReturnsParseFailure(string? log)
    {
        // Arrange (input via theory parameter)

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.ParseFailure>();
    }
}
