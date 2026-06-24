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

    [Fact]
    public void WhenLogExceeds64KB_TruncatesAndStillParsesLastJsonLine()
    {
        // Arrange
        string jsonLine = """{"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"num_turns":5,"result":"All done.","session_id":"abc","terminal_reason":"stop_reason"}""";
        string padding = new string('x', 70_000) + "\n";
        string log = padding + jsonLine;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenJsonLineExceeds4KB_ReturnsParseFailure()
    {
        // Arrange
        string oversizedValue = new string('x', 4_100);
        string jsonLine = $$$"""{"type":"result","terminal_reason":"blocking_limit","result":"{{{oversizedValue}}}"}""";

        // Act
        ContainerOutputParseResult result = _sut.Parse(jsonLine, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.ParseFailure>();
    }

    [Fact]
    public void WhenApiErrorStatus429WithSuccessSubtypeAndCompletedReason_ReturnsUsageLimited()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"num_turns":5,"result":"All done.","session_id":"abc","terminal_reason":"completed","api_error_status":429}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(529)]
    public void WhenNon429ApiErrorStatusAndNonAllowlistTerminalReason_ReturnsNormalExit(int apiErrorStatus)
    {
        // Arrange
        string log = $$"""
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Done.","session_id":"abc","terminal_reason":"completed","api_error_status":{{apiErrorStatus}}}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenWallClockResetTimeInFuture_ReturnsUsageLimitedWithParsedUtcTime()
    {
        // Arrange
        // Use a time far in the future to ensure it's always "next occurrence today"
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets 11:59pm (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        // The reset time should be today or tomorrow at 11:59pm UTC
        limited.ResetsAt.Hour.ShouldBe(23);
        limited.ResetsAt.Minute.ShouldBe(59);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenWallClockResetTimeInPast_ResolvesToNextDayOccurrence()
    {
        // Arrange
        // Use a time guaranteed to be in the past: 1 hour ago
        DateTimeOffset oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        string pastTime = oneHourAgo.ToString("h:mmtt", System.Globalization.CultureInfo.InvariantCulture)
            .ToLowerInvariant();
        string log = $$$"""
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets {{{pastTime}}} (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBeGreaterThan(utcNow);
        limited.ResetsAt.Hour.ShouldBe(oneHourAgo.Hour);
        limited.ResetsAt.Minute.ShouldBe(oneHourAgo.Minute);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenApiErrorStatusIsJsonString429_ReturnsUsageLimited()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Done.","session_id":"abc","terminal_reason":"completed","api_error_status":"429"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
    }

    [Fact]
    public void WhenApiErrorStatus429AndResetTextHasNoTimestamp_FallsBackToDefaultCooldown()
    {
        // Arrange
        DateTimeOffset before = DateTimeOffset.UtcNow;
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Usage limit exceeded. No timestamp available.","session_id":"abc","terminal_reason":"completed","api_error_status":429}
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
    public void WhenWallClockResetTimeWithUtcAnnotation_ResetOffsetIsExplicitlyUtc()
    {
        // Arrange
        // "resets 11:59pm (UTC)" — the (UTC) annotation must produce offset Zero, not local offset
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets 11:59pm (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
        limited.ResetsAt.Hour.ShouldBe(23);
        limited.ResetsAt.Minute.ShouldBe(59);
    }

    [Fact]
    public void WhenApiErrorStatusIsOutOfInt32Range_IsNotClassifiedAsUsageLimit()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"Done.","session_id":"abc","terminal_reason":"completed","api_error_status":99999999999}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenWallClockResetTimeHasNoUtcAnnotation_FallsBackToDefaultCooldown()
    {
        // Arrange
        // "resets 11:59pm" without (UTC) must NOT be parsed as wall-clock UTC time
        DateTimeOffset before = DateTimeOffset.UtcNow;
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets 11:59pm","session_id":"xyz","terminal_reason":"blocking_limit"}
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
    public void WhenBootstrapSentinelPresent_NoClauseJson_ReturnsWorkerBootstrapFailed()
    {
        // Arrange
        string log = """
            Cloning repository...
            FOUNDRY_BOOTSTRAP_FAILED stage=clone unable to reach remote
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.WorkerBootstrapFailed failed = result.ShouldBeOfType<ContainerOutputParseResult.WorkerBootstrapFailed>();
        failed.Detail.ShouldContain("clone");
    }

    [Theory]
    [InlineData("clone")]
    [InlineData("auth")]
    [InlineData("branch")]
    public void WhenBootstrapSentinelWithKnownStage_ReturnsWorkerBootstrapFailed(string stage)
    {
        // Arrange
        string log = $"FOUNDRY_BOOTSTRAP_FAILED stage={stage} some detail message";

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.WorkerBootstrapFailed failed = result.ShouldBeOfType<ContainerOutputParseResult.WorkerBootstrapFailed>();
        failed.Detail.ShouldContain(stage);
    }

    [Fact]
    public void WhenUsageLimitedJsonPlusBootstrapSentinel_UsageLimitedWins()
    {
        // Arrange
        string log = """
            FOUNDRY_BOOTSTRAP_FAILED stage=clone spoofed sentinel
            {"type":"result","subtype":"error","is_error":true,"duration_ms":300,"num_turns":1,"result":"Usage limit hit. Resets at 2026-06-18T16:30:00+00:00.","session_id":"abc","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
    }

    [Fact]
    public void WhenNormalExitJsonPlusBootstrapSentinel_NormalExitWins()
    {
        // Arrange
        string log = """
            FOUNDRY_BOOTSTRAP_FAILED stage=auth spoofed sentinel
            {"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"num_turns":5,"result":"All done.","session_id":"abc","terminal_reason":"stop_reason"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenBootstrapSentinelWithUnknownStage_ReturnsParseFailure()
    {
        // Arrange
        string log = "FOUNDRY_BOOTSTRAP_FAILED stage=bogus unknown stage token";

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.ParseFailure>();
    }

    [Fact]
    public void WhenBootstrapSentinelDetailExceedsCap_DetailIsTruncated()
    {
        // Arrange
        string longDetail = new string('x', 600);
        string log = $"FOUNDRY_BOOTSTRAP_FAILED stage=clone {longDetail}";

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.WorkerBootstrapFailed failed = result.ShouldBeOfType<ContainerOutputParseResult.WorkerBootstrapFailed>();
        failed.Detail.Length.ShouldBeLessThanOrEqualTo(500);
    }
}
