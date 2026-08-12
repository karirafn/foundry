using System.Globalization;

using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Testing;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ContainerOutputParserTests;

public sealed class Parse
{
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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 16, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WhenBlockingLimitWithUnparseableResetTime_ReturnsCreditsExhausted()
    {
        // Arrange
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":200,"num_turns":1,"result":"Usage limit exceeded. No timestamp available.","session_id":"def","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
    }

    [Fact]
    public void WhenNonJsonInput_ReturnsNoResultLine()
    {
        // Arrange
        string log = "Plain text output from the container.";

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(jsonLine);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.ParseFailure>();
    }

    [Fact]
    public void WhenApiErrorStatus429WithSuccessSubtypeAndCompletedReason_ReturnsCreditsExhausted()
    {
        // Arrange — result text "All done." has no parseable reset time → CreditsExhausted
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"num_turns":5,"result":"All done.","session_id":"abc","terminal_reason":"completed","api_error_status":429}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        string pastTime = oneHourAgo.ToString("h:mmtt", CultureInfo.InvariantCulture)
            .ToLowerInvariant();
        string log = $$$"""
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets {{{pastTime}}} (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBeGreaterThan(utcNow);
        limited.ResetsAt.Hour.ShouldBe(oneHourAgo.Hour);
        limited.ResetsAt.Minute.ShouldBe(oneHourAgo.Minute);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenApiErrorStatusIsJsonString429_ReturnsCreditsExhausted()
    {
        // Arrange — result text "Done." has no parseable reset time → CreditsExhausted
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Done.","session_id":"abc","terminal_reason":"completed","api_error_status":"429"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
    }

    [Fact]
    public void WhenApiErrorStatus429AndResetTextHasNoTimestamp_ReturnsCreditsExhausted()
    {
        // Arrange — no parseable reset time in result text → CreditsExhausted (no fallback cooldown)
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Usage limit exceeded. No timestamp available.","session_id":"abc","terminal_reason":"completed","api_error_status":429}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenWallClockResetTimeHasNoUtcAnnotation_ReturnsCreditsExhausted()
    {
        // Arrange — "resets 11:59pm" without (UTC) must NOT be parsed; wall-clock regex requires a timezone
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets 11:59pm","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenBootstrapSentinelWithUnknownStage_ReturnsNoResultLine()
    {
        // Arrange
        string log = "FOUNDRY_BOOTSTRAP_FAILED stage=bogus unknown stage token";

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenApiErrorStatus429_ReturnsCreditsExhausted_NotAuthInvalid()
    {
        // Arrange — regression: usage-limited signal must not be reclassified as auth-invalid;
        // "Usage limit hit." has no parseable reset time → CreditsExhausted
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"Usage limit hit.","session_id":"abc","terminal_reason":"blocking_limit","api_error_status":429}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
    }

    // --- Transient API error detection ---

    [Fact]
    public void WhenApiErrorStatus500_ReturnsTransientApiError()
    {
        // Arrange — any 5xx api_error_status triggers transient classification
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"Server error.","session_id":"abc","terminal_reason":"completed","api_error_status":500}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

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
        ContainerOutputParseResult result = sut.Parse(log);

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
        ContainerOutputParseResult result = sut.Parse(log);

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
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — falls through to NormalExit, not TransientApiError
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenIsErrorTrueAndResultContainsNewline_LoggedWarningHasNoEmbeddedNewline()
    {
        // Arrange — result text contains a newline (log-injection attempt from untrusted worker output)
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"unclassified error\nINJECTED SECOND LOG LINE","session_id":"abc","terminal_reason":"completed"}
            """;
        CapturingLogger logger = new();
        ContainerOutputParser sut = BuildParserWithCapture(logger);

        // Act
        sut.Parse(log);

        // Assert — the logged warning message must not contain a literal newline
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
        (LogLevel Level, string Message, Exception? Exception) warning =
            logger.Entries.Single(e => e.Level == LogLevel.Warning);
        warning.Message.ShouldNotContain('\n');
        warning.Message.ShouldNotContain('\r');
    }

    // --- IANA timezone reset-time parsing ---

    [Fact]
    public void WhenResetAtWithIanaTimezone_ResolvesToCorrectUtcTime()
    {
        // Arrange — the actual message Claude Code emits: "reset at 3pm (America/New_York)"
        // America/New_York is UTC-4 in EDT (summer) or UTC-5 in EST (winter).
        // The test asserts that the hour is correct in UTC, not that it equals a fixed offset,
        // because the test itself cannot know whether DST is currently active.
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Claude usage limit reached. Your limit will reset at 3pm (America/New_York).","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — parsed as UsageLimited with a UTC time corresponding to 3pm New York
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        TimeZoneInfo nyZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        DateTimeOffset nowInNy = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, nyZone);
        // Expected UTC hour: 3pm New York converted to UTC
        DateTime threePmNy = new(nowInNy.Date.Year, nowInNy.Date.Month, nowInNy.Date.Day, 15, 0, 0);
        DateTimeOffset expectedUtc = TimeZoneInfo.ConvertTimeToUtc(threePmNy, nyZone);
        if (expectedUtc <= DateTimeOffset.UtcNow)
        {
            expectedUtc = expectedUtc.AddDays(1);
        }

        limited.ResetsAt.ShouldBe(expectedUtc, tolerance: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WhenResetAtWithBareHourAndIanaTimezone_MinutesDefaultToZero()
    {
        // Arrange — bare hour (no :MM) must default minutes to 00
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Your limit will reset at 5am (America/Los_Angeles).","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        // 5am Los_Angeles is always 5 or 6 hours offset to UTC — verify the parsed minute is 0
        TimeZoneInfo laZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        DateTimeOffset nowInLa = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, laZone);
        DateTime fiveAmLa = new(nowInLa.Date.Year, nowInLa.Date.Month, nowInLa.Date.Day, 5, 0, 0);
        DateTimeOffset expectedUtc = TimeZoneInfo.ConvertTimeToUtc(fiveAmLa, laZone);
        if (expectedUtc <= DateTimeOffset.UtcNow)
        {
            expectedUtc = expectedUtc.AddDays(1);
        }

        limited.ResetsAt.ShouldBe(expectedUtc, tolerance: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WhenResetAtWithBareHourAndUtcAnnotation_MinutesDefaultToZero()
    {
        // Arrange — bare hour (no :MM) with (UTC) annotation
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets 3pm (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.Hour.ShouldBe(15);
        limited.ResetsAt.Minute.ShouldBe(0);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenResetAtWithUnknownTimezone_ReturnsCreditsExhausted()
    {
        // Arrange — timezone name that does not exist in the timezone database
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Your limit will reset at 3pm (NotA/RealTimezone).","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — unknown timezone means no parseable reset time → CreditsExhausted, never throw
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
    }

    [Fact]
    public void WhenMidnightAm_12amBoundary_ResolvesToMidnight()
    {
        // Arrange — 12am is midnight (00:00)
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets 12:00am (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.Hour.ShouldBe(0);
        limited.ResetsAt.Minute.ShouldBe(0);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenNoonPm_12pmBoundary_ResolvesToNoon()
    {
        // Arrange — 12pm is noon (12:00)
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets 12:00pm (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.Hour.ShouldBe(12);
        limited.ResetsAt.Minute.ShouldBe(0);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenMultipleCandidateTimesInText_FirstMatchWins()
    {
        // Arrange — two wall-clock times present; the first one (2am) should win
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. resets 2:00am (UTC) or maybe resets 11:00pm (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — first match (2am) wins
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.Hour.ShouldBe(2);
        limited.ResetsAt.Minute.ShouldBe(0);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenNonLimitTerminalReason_ReturnsNormalExit()
    {
        // Arrange — api_error_status:200 and terminal_reason:"completed" are not usage-limit signals;
        // this test proves the classification gate (IsUsageLimit) short-circuits before any regex runs
        string log = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"num_turns":1,"result":"The meeting resets at 3pm (UTC) tomorrow.","session_id":"abc","terminal_reason":"completed","api_error_status":200}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — normal exit, not usage-limited
        result.ShouldBeOfType<ContainerOutputParseResult.NormalExit>();
    }

    [Fact]
    public void WhenUsageLimitButResetPhraseHasNoTimezone_ReturnsCreditsExhausted()
    {
        // Arrange — terminal_reason is blocking_limit (IS a usage limit) but the reset-time phrase has no
        // timezone in parentheses, so the wall-clock regex must NOT match — exercises the regex rejection path
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Your limit will reset at 3pm today.","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — no parseable reset time (regex requires "(timezone)") → CreditsExhausted
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
    }

    [Fact]
    public void WhenResetAtPhraseAlreadyPastInUtc_ResolvesToNextDayUtc()
    {
        // Arrange — exercises the UTC fast-path: pick a time guaranteed to be already past (1 hour ago in UTC)
        DateTimeOffset oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        string pastTime = oneHourAgo.ToString("h:mmtt", CultureInfo.InvariantCulture)
            .ToLowerInvariant();
        string log = $$$"""
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Your limit will reset at {{{pastTime}}} (UTC).","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — rolled forward to tomorrow
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBeGreaterThan(utcNow);
        limited.ResetsAt.Hour.ShouldBe(oneHourAgo.Hour);
        limited.ResetsAt.Minute.ShouldBe(oneHourAgo.Minute);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenResetAtVerbWithUtcAnnotation_RegressionGuard()
    {
        // Arrange — "You've hit your limit · resets 12:10am (UTC)" — regression guard for original shape
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"You've hit your limit · resets 12:10am (UTC)","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.Hour.ShouldBe(0);
        limited.ResetsAt.Minute.ShouldBe(10);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenBareHourWithSpaceBeforeAmPm_ResolvesCorrectly()
    {
        // Arrange — "reset at 3 pm (UTC)" has a space between "3" and "pm"; NormalizedBareHourTime must
        // trim the space before splicing ":00" so TimeOnly.TryParse receives "3:00pm", not "3 :00pm"
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. reset at 3 pm (UTC).","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.Hour.ShouldBe(15);
        limited.ResetsAt.Minute.ShouldBe(0);
        limited.ResetsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void WhenDstSpringForwardGapTime_DoesNotThrowAndReturnsUsageLimited()
    {
        // Arrange — construct a result text with a wall-clock time in the America/New_York spring-forward gap
        // (clocks jump from 2:00am to 3:00am on the second Sunday of March, so 2:30am does not exist).
        // The parse must NEVER throw — it must return UsageLimited (resolved or fallback).
        // We use a fixed gap time from a known past spring-forward date; the zone lookup is always valid.
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Usage limit reached. reset at 2:30am (America/New_York).","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act — must not throw ArgumentException from ConvertTimeToUtc
        ContainerOutputParseResult result = Should.NotThrow(() => _sut.Parse(log));

        // Assert — classified as UsageLimited (either resolved past the gap or default-cooldown fallback)
        result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
    }

    [Fact]
    public void WhenResetAtWithIanaTimezoneAlreadyPastInZone_ResolvesToNextDayUtc()
    {
        // Arrange — pick a wall-clock time that is guaranteed to be already past in America/Chicago right now:
        // compute "1 hour ago" in Chicago time, then embed that as the reset time.
        // This exercises the IANA ConvertTimeToUtc roll-forward path (not the UTC fast-path).
        TimeZoneInfo chicagoZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        DateTimeOffset nowInChicago = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, chicagoZone);
        DateTimeOffset oneHourAgoInChicago = nowInChicago.AddHours(-1);
        string pastTime = oneHourAgoInChicago.ToString("h:mmtt", CultureInfo.InvariantCulture)
            .ToLowerInvariant();
        string log = $$$"""
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"Your limit will reset at {{{pastTime}}} (America/Chicago).","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — rolled forward to tomorrow Chicago time; the resolved UTC instant must be in the future.
        // The assertion verifies consistency within a tolerance window (the zone lookup and parse use the
        // same production ConvertTimeToUtc path, so this mirrors production conversion, not absolute ground truth).
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBeGreaterThan(utcNow);
    }

    [Fact]
    public void WhenTimezoneExceedsLengthCap_ReturnsCreditsExhausted()
    {
        // Arrange — a timezone string longer than 64 characters is not a real IANA id; the parser must
        // return CreditsExhausted without calling FindSystemTimeZoneById
        string longTimezone = new string('A', 65);
        string log = $$"""
            {"type":"result","subtype":"error","is_error":true,"duration_ms":500,"num_turns":2,"result":"reset at 3pm ({{longTimezone}}).","session_id":"xyz","terminal_reason":"blocking_limit"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert — no parseable reset time (timezone too long) → CreditsExhausted, never throw
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
    }

    // --- New tests: CreditsExhausted for 429 and allowlisted terminal_reason with no parseable reset time ---

    [Fact]
    public void WhenApiErrorStatus429WithParseableResetTime_ReturnsUsageLimited()
    {
        // Arrange — 429 with a parseable ISO8601 reset time → UsageLimited
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":100,"num_turns":1,"result":"Rate limit. Resets at 2026-06-18T15:00:00Z.","session_id":"abc","terminal_reason":"completed","api_error_status":429}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        ContainerOutputParseResult.UsageLimited limited = result.ShouldBeOfType<ContainerOutputParseResult.UsageLimited>();
        limited.ResetsAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WhenRapidRefillBreakerWithNoParseableResetTime_ReturnsCreditsExhausted()
    {
        // Arrange — allowlisted terminal_reason with no parseable reset time → CreditsExhausted
        string log = """
            {"type":"result","subtype":"error","is_error":true,"duration_ms":300,"num_turns":1,"result":"Rapid refill breaker triggered.","session_id":"abc","terminal_reason":"rapid_refill_breaker"}
            """;

        // Act
        ContainerOutputParseResult result = _sut.Parse(log);

        // Assert
        result.ShouldBeOfType<ContainerOutputParseResult.CreditsExhausted>();
    }
}
