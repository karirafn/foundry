using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Testing;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ContainerOutputParserTests;

public sealed class Parse
{
    private const int DefaultCooldownMinutes = 60;

    private readonly IContainerOutputParser _sut = new ContainerOutputParser(
        NullLogger<ContainerOutputParser>.Instance);

    private static ContainerOutputParser BuildParserWithCapture(CapturingLogger logger)
    {
        return new ContainerOutputParser(new CapturingLoggerAdapter(logger));
    }

    private sealed class CapturingLoggerAdapter(CapturingLogger inner) : ILogger<ContainerOutputParser>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

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
    public void WhenNonJsonInput_ReturnsNoResultLine()
    {
        // Arrange
        string log = "Plain text output from the container.";

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NoResultLine>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenNullOrEmptyInput_ReturnsNoResultLine(string? log)
    {
        // Arrange (input via theory parameter)

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NoResultLine>();
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
    public void WhenApiErrorStatusIn5xx_ReturnsTransientApiError(int apiErrorStatus)
    {
        // Arrange
        string log = $$"""
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Done.","session_id":"abc","terminal_reason":"completed","api_error_status":{{apiErrorStatus}}}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TransientApiError>();
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
    public void WhenBootstrapSentinelWithUnknownStage_ReturnsNoResultLine()
    {
        // Arrange
        string log = "FOUNDRY_BOOTSTRAP_FAILED stage=bogus unknown stage token";

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NoResultLine>();
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

    [Fact]
    public void WhenJsonLineHasDockerTimestampPrefix_ParseStillClassifiesResult()
    {
        // Arrange — realistic Docker-timestamped output ending with a usage-limit JSON line
        string log = """
            2026-06-29T21:24:01.000000000Z Starting claude...
            2026-06-29T21:24:02.123456789Z Cloning repository...
            2026-06-29T21:24:05.123456789Z {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. Resets at 2026-06-29T22:00:00Z.","session_id":"abc","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBe(new DateTimeOffset(2026, 6, 29, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WhenApiErrorStatus401_ReturnsAuthInvalid()
    {
        // Arrange
        // Shape assumed: api_error_status == 401 as the primary signal.
        // No real fixture was found; predicate mirrors the 429 pattern (api_error_status field).
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":0,"result":"Authentication error.","session_id":"abc","terminal_reason":"error","api_error_status":401}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.AuthInvalid>();
    }

    [Fact]
    public void WhenErrorTypeIsAuthenticationError_ReturnsAuthInvalid()
    {
        // Arrange
        // Secondary guard: error.type == "authentication_error" (shape assumed — no real fixture found).
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":0,"result":"Authentication error.","session_id":"abc","terminal_reason":"error","error":{"type":"authentication_error","message":"Invalid API key"}}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.AuthInvalid>();
    }

    [Fact]
    public void WhenApiErrorStatus401AndTerminalReasonIsBlockingLimit_UsageLimitedWins()
    {
        // Arrange
        // Precedence: usage-limit check runs first; 429/blocking_limit wins over 401
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":0,"result":"Limit hit. Resets at 2026-06-18T15:00:00Z.","session_id":"abc","terminal_reason":"blocking_limit","api_error_status":401}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
    }

    [Fact]
    public void WhenNormalExitJson_StillReturnsNormalExit_NotAuthInvalid()
    {
        // Arrange — regression: normal exit must not be classified as auth-invalid
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"num_turns":5,"result":"All done.","session_id":"abc","terminal_reason":"stop_reason"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenApiErrorStatus429_StillReturnsUsageLimited_NotAuthInvalid()
    {
        // Arrange — regression: usage-limited must not be reclassified as auth-invalid
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"Usage limit hit.","session_id":"abc","terminal_reason":"blocking_limit","api_error_status":429}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
    }

    // --- Transient API error detection ---

    [Fact]
    public void WhenApiErrorStatus529_ReturnsTransientApiError()
    {
        // Arrange — run 6EB72F0F-0DC5-41AC-98D4-DB8C4D78E7CA fixture
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"API Error: 529 Overloaded. This is a server-side issue, usually temporary...","session_id":"abc","terminal_reason":"completed","api_error_status":529}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TransientApiError>();
    }

    [Fact]
    public void WhenApiErrorStatus500_ReturnsTransientApiError()
    {
        // Arrange — any 5xx api_error_status triggers transient classification
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Server error.","session_id":"abc","terminal_reason":"completed","api_error_status":500}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TransientApiError>();
    }

    [Fact]
    public void WhenApiErrorStatus599_ReturnsTransientApiError()
    {
        // Arrange — upper boundary of 5xx range
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Server error.","session_id":"abc","terminal_reason":"completed","api_error_status":599}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TransientApiError>();
    }

    [Fact]
    public void WhenIsErrorAndConnectionClosedPhrase_ReturnsTransientApiError()
    {
        // Arrange — run 6BDD98F4-5BCB-4E72-A05A-951BF3741770 fixture:
        //   api_error_status: null, is_error: true, subtype: "success", terminal_reason: "completed"
        string log = """
            {"type":"result","subtype":"success","is_error":true,"duration_ms":100,"num_turns":1,"result":"API Error: Connection closed mid-response. The response above may be incomplete.","session_id":"abc","terminal_reason":"completed"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TransientApiError>();
    }

    [Fact]
    public void WhenIsErrorAndOverloadedPhrase_ReturnsTransientApiError()
    {
        // Arrange — is_error:true with "API Error: 529 Overloaded" phrase (no api_error_status field)
        string log = """
            {"type":"result","subtype":"success","is_error":true,"duration_ms":100,"num_turns":1,"result":"API Error: 529 Overloaded. This is a server-side issue, usually temporary...","session_id":"abc","terminal_reason":"completed"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.TransientApiError>();
    }

    [Fact]
    public void WhenIsErrorTrueAndNoKnownCategory_LogsWarningAndReturnsNormalExit()
    {
        // Arrange — bare is_error:true with unrecognised result text
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"Something completely unknown happened.","session_id":"abc","terminal_reason":"completed"}
            """;
        CapturingLogger logger = new();
        ContainerOutputParser sut = BuildParserWithCapture(logger);

        // Act
        ContainerOutputParseResult result = sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void WhenIsErrorFalseAndUnrecognisedResult_DoesNotLogWarning()
    {
        // Arrange — regression: warning guard must be gated on is_error:true only
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Something completely unknown happened.","session_id":"abc","terminal_reason":"completed"}
            """;
        CapturingLogger logger = new();
        ContainerOutputParser sut = BuildParserWithCapture(logger);

        // Act
        ContainerOutputParseResult result = sut.Parse(log, DefaultCooldownMinutes);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
        logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void WhenIsErrorTrueAndBareNoApiErrorStatus_NotTransient_ReturnsNormalExit()
    {
        // Arrange — bare is_error:true with no api_error_status and no transient phrase:
        //   must NOT be classified as transient
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"Some non-transient error text.","session_id":"abc","terminal_reason":"completed"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log, DefaultCooldownMinutes);

        // Assert — falls through to NormalExit, not TransientApiError
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }
}
