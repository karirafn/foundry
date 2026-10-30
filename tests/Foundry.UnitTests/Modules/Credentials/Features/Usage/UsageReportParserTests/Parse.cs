using Foundry.Modules.Credentials.Features.Usage;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsageReportParserTests;

public sealed class Parse
{
    private const string AccountEmail = "user@example.com";
    private static readonly DateTimeOffset CapturedAt = new(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------------
    // Real rate_limit_event fixtures (CLI 2.1.302+)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Full stream-json from a real turn on CLI 2.1.302: system init + assistant message +
    /// rate_limit_event with both five_hour and seven_day windows, then a result.
    /// The rate_limit_event carries rateLimitType="seven_day" so seven_day is IsActive.
    /// </summary>
    private const string FullRateLimitEventLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-8","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3}},"parent_tool_use_id":null,"session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1793527200,"rateLimitType":"seven_day","overageStatus":"allowed_warning","overageResetsAt":1793491200,"isUsingOverage":true,"overageInUse":true,"unifiedWindows":{"five_hour":{"utilization":0.06,"resetsAt":1793399400},"seven_day":{"utilization":1,"resetsAt":1793527200}}},"uuid":"dab406c8-9ba0-40d4-b473-fcbdabd857e3","session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":1350,"duration_api_ms":1000,"num_turns":1,"result":"ok","total_cost_usd":0.001,"usage":{"input_tokens":10,"output_tokens":3},"session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event where rateLimitType="five_hour", so five_hour is IsActive.
    /// </summary>
    private const string FiveHourActiveLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3}},"session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793399400,"rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.85,"resetsAt":1793399400},"seven_day":{"utilization":0.30,"resetsAt":1793527200}}},"uuid":"aaa","session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"result":"ok","session_id":"abc123"}
        """;

    /// <summary>
    /// A stream-json log from an actual turn where no rate_limit_event is emitted (low usage / API-key mode).
    /// </summary>
    private const string NoRateLimitEventLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3}},"session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"result":"ok","session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event present but unifiedWindows is absent (CLI &lt; 2.1.302).
    /// Parser must return NoUsageReport — not Unparseable — because the absence of
    /// unifiedWindows means we cannot produce useful meter data.
    /// </summary>
    private const string NoUnifiedWindowsLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1793527200,"rateLimitType":"seven_day","isUsingOverage":true},"uuid":"bbb","session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"result":"ok","session_id":"abc123"}
        """;

    /// <summary>A log with malformed JSON on one of the lines.</summary>
    private const string MalformedJsonLog = """
        {"type":"system","subtype":"init"}
        {INVALID JSON CANNOT PARSE THIS
        """;

    /// <summary>
    /// A rate_limit_event where a window field has the wrong token type (string where number expected).
    /// The parser must not throw — return Unparseable.
    /// </summary>
    private const string TypeMismatchedRateLimitEventLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1793527200,"rateLimitType":"seven_day","unifiedWindows":{"five_hour":{"utilization":"high","resetsAt":1793399400},"seven_day":{"utilization":1,"resetsAt":1793527200}}},"uuid":"ccc","session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event where rateLimitType does not match any known window key.
    /// The window with the highest utilization must be marked IsActive.
    /// </summary>
    private const string UnknownRateLimitTypeLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793527200,"rateLimitType":"monthly","unifiedWindows":{"five_hour":{"utilization":0.06,"resetsAt":1793399400},"seven_day":{"utilization":0.90,"resetsAt":1793527200}}},"uuid":"ddd","session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"result":"ok","session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event where rateLimitType is absent (null).
    /// The highest-utilization fallback must still fire, marking the active window.
    /// </summary>
    private const string NullRateLimitTypeLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793527200,"unifiedWindows":{"five_hour":{"utilization":0.06,"resetsAt":1793399400},"seven_day":{"utilization":0.90,"resetsAt":1793527200}}},"uuid":"eee","session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"result":"ok","session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event where a window's resetsAt value is wildly out of the
    /// DateTimeOffset.FromUnixTimeSeconds valid range (-62135596800 to 253402300799).
    /// The parser must return Unparseable without throwing.
    /// </summary>
    private const string OutOfRangeResetsAtLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793527200,"rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.50,"resetsAt":-9999999999999},"seven_day":{"utilization":0.30,"resetsAt":1793527200}}},"uuid":"fff","session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event where a window's utilization field is absent (null).
    /// The parser must return Unparseable rather than silently defaulting to 0.
    /// </summary>
    private const string AbsentUtilizationLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793527200,"rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"resetsAt":1793399400},"seven_day":{"utilization":0.30,"resetsAt":1793527200}}},"uuid":"ggg","session_id":"abc123"}
        """;

    /// <summary>
    /// A log with two rate_limit_event lines. The parser must use the LAST event that
    /// carries unifiedWindows — the CLI may emit a preamble event before the real one.
    /// The first event has five_hour active at 0.50; the last has seven_day active at 0.90.
    /// </summary>
    private const string TwoEventLinesLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793399400,"rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.50,"resetsAt":1793399400},"seven_day":{"utilization":0.20,"resetsAt":1793527200}}},"uuid":"hhh","session_id":"abc123"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3}},"session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793527200,"rateLimitType":"seven_day","unifiedWindows":{"five_hour":{"utilization":0.06,"resetsAt":1793399400},"seven_day":{"utilization":0.90,"resetsAt":1793527200}}},"uuid":"iii","session_id":"abc123"}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"result":"ok","session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event with utilization exactly 0.8 on the five_hour window.
    /// Severity must be "warning" at the boundary.
    /// </summary>
    private const string ExactWarningThresholdLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793399400,"rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.8,"resetsAt":1793399400},"seven_day":{"utilization":0.30,"resetsAt":1793527200}}},"uuid":"jjj","session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event with utilization 0.79 on the five_hour window.
    /// Severity must be "normal" just below the warning threshold.
    /// </summary>
    private const string BelowWarningThresholdLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793399400,"rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.79,"resetsAt":1793399400},"seven_day":{"utilization":0.30,"resetsAt":1793527200}}},"uuid":"kkk","session_id":"abc123"}
        """;

    /// <summary>
    /// A rate_limit_event with utilization exactly 1.0 on the five_hour window.
    /// Severity must be "critical" at the boundary.
    /// </summary>
    private const string ExactCriticalThresholdLog = """
        {"type":"system","subtype":"init","session_id":"abc123"}
        {"type":"rate_limit_event","rate_limit_info":{"status":"active","resetsAt":1793399400,"rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":1.0,"resetsAt":1793399400},"seven_day":{"utilization":0.30,"resetsAt":1793527200}}},"uuid":"lll","session_id":"abc123"}
        """;

    // ---------------------------------------------------------------------------
    // Happy-path: both windows parsed correctly
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenRateLimitEventWithBothWindows_ReturnsParsedWithSessionAndWeeklyAllLimits()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageSnapshot snapshot = parsed.Snapshot;

        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.AccountEmail.ShouldBe(AccountEmail),
            () => snapshot.CapturedAt.ShouldBe(CapturedAt),
            () => snapshot.LastRefreshFailedAt.ShouldBeNull(),
            () => snapshot.Limits.Count.ShouldBe(2));
    }

    [Fact]
    public void WhenRateLimitEventPresent_FiveHourWindowMapsToSessionKind()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        session.ShouldSatisfyAllConditions(
            // utilization=0.06 → percent=6
            () => session.Percent.ShouldBe(6m),
            () => session.ResetsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1793399400)),
            () => session.ModelDisplayName.ShouldBeNull(),
            () => session.IsActive.ShouldBeFalse());
    }

    [Fact]
    public void WhenRateLimitEventPresent_SevenDayWindowMapsToWeeklyAllKind()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit weeklyAll = parsed.Snapshot.Limits.Single(l => l.Kind == "weekly_all");
        weeklyAll.ShouldSatisfyAllConditions(
            // utilization=1 → percent=100
            () => weeklyAll.Percent.ShouldBe(100m),
            () => weeklyAll.ResetsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1793527200)),
            () => weeklyAll.ModelDisplayName.ShouldBeNull(),
            () => weeklyAll.IsActive.ShouldBeTrue());
    }

    // ---------------------------------------------------------------------------
    // Severity derivation (server-side mapping)
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenUtilizationIsOne_SeverityIsCritical()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert — seven_day has utilization=1.0
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit weeklyAll = parsed.Snapshot.Limits.Single(l => l.Kind == "weekly_all");
        weeklyAll.Severity.ShouldBe("critical");
    }

    [Fact]
    public void WhenUtilizationIsEightyFivePercent_SeverityIsWarning()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FiveHourActiveLog, AccountEmail, CapturedAt);

        // Assert — five_hour has utilization=0.85
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        session.Severity.ShouldBe("warning");
    }

    [Fact]
    public void WhenUtilizationIsLow_SeverityIsNormal()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert — five_hour has utilization=0.06
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        session.Severity.ShouldBe("normal");
    }

    // ---------------------------------------------------------------------------
    // IsActive derivation
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenRateLimitTypeIsFiveHour_FiveHourWindowIsActive()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FiveHourActiveLog, AccountEmail, CapturedAt);

        // Assert
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        UsageRateLimit weeklyAll = parsed.Snapshot.Limits.Single(l => l.Kind == "weekly_all");
        session.IsActive.ShouldBeTrue();
        weeklyAll.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void WhenRateLimitTypeIsSevenDay_SevenDayWindowIsActive()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert — exactly one active window: weekly_all active, session inactive.
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit weeklyAll = parsed.Snapshot.Limits.Single(l => l.Kind == "weekly_all");
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        weeklyAll.IsActive.ShouldBeTrue();
        session.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void WhenRateLimitTypeIsUnknown_WindowWithHighestUtilizationIsActive()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(UnknownRateLimitTypeLog, AccountEmail, CapturedAt);

        // Assert — seven_day has utilization=0.90, five_hour=0.06, so seven_day is active
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit weeklyAll = parsed.Snapshot.Limits.Single(l => l.Kind == "weekly_all");
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        weeklyAll.IsActive.ShouldBeTrue();
        session.IsActive.ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // ExtraUsage is always null (rate_limit_event carries no credit amounts)
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenRateLimitEventParsed_ExtraUsageIsAlwaysNull()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert — rate_limit_event carries no credit amounts; follow-up issue covers this.
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        parsed.Snapshot.ExtraUsage.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------
    // NoUsageReport paths
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenNoRateLimitEventInLog_ReturnsNoUsageReport()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(NoRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.NoUsageReport>();
    }

    [Fact]
    public void WhenRateLimitEventMissingUnifiedWindows_ReturnsNoUsageReport()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(NoUnifiedWindowsLog, AccountEmail, CapturedAt);

        // Assert — unifiedWindows absent means CLI < 2.1.302; treat as no usable data
        result.ShouldBeOfType<UsageParseResult.NoUsageReport>();
    }

    // ---------------------------------------------------------------------------
    // Unparseable paths
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenMalformedJson_ReturnsUnparseable()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(MalformedJsonLog, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    [Fact]
    public void WhenRateLimitInfoHasWrongTokenType_ReturnsUnparseable()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(TypeMismatchedRateLimitEventLog, AccountEmail, CapturedAt);

        // Assert — type mismatch must not throw; it must produce Unparseable
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    // ---------------------------------------------------------------------------
    // IsActive: null rateLimitType falls through to highest-utilization fallback
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenRateLimitTypeIsAbsent_HighestUtilizationWindowIsActive()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(NullRateLimitTypeLog, AccountEmail, CapturedAt);

        // Assert — seven_day has utilization=0.90, so it must be active; session must be inactive
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit weeklyAll = parsed.Snapshot.Limits.Single(l => l.Kind == "weekly_all");
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        weeklyAll.IsActive.ShouldBeTrue();
        session.IsActive.ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // Robustness: out-of-range resetsAt and absent utilization → Unparseable
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenResetsAtIsOutOfRange_ReturnsUnparseable()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(OutOfRangeResetsAtLog, AccountEmail, CapturedAt);

        // Assert — out-of-range Unix timestamp must not throw; it must produce Unparseable
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    [Fact]
    public void WhenUtilizationIsAbsent_ReturnsUnparseable()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(AbsentUtilizationLog, AccountEmail, CapturedAt);

        // Assert — absent utilization must not silently default to 0; it must produce Unparseable
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    // ---------------------------------------------------------------------------
    // Multiple event lines: last wins
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenTwoEventLines_LastEventWins()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(TwoEventLinesLog, AccountEmail, CapturedAt);

        // Assert — the last rate_limit_event has rateLimitType="seven_day" so seven_day is active
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit weeklyAll = parsed.Snapshot.Limits.Single(l => l.Kind == "weekly_all");
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        weeklyAll.IsActive.ShouldBeTrue();
        session.IsActive.ShouldBeFalse();
        // The last event had seven_day utilization=0.90 → percent=90, not the first event's 20
        weeklyAll.Percent.ShouldBe(90m);
    }

    // ---------------------------------------------------------------------------
    // Severity boundary: exactly 0.8 → warning, 0.79 → normal, 1.0 → critical
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenUtilizationIsExactlyPointEight_SeverityIsWarning()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(ExactWarningThresholdLog, AccountEmail, CapturedAt);

        // Assert — 0.8 is the warning threshold boundary (inclusive)
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        session.Severity.ShouldBe("warning");
    }

    [Fact]
    public void WhenUtilizationIsPointSevenNine_SeverityIsNormal()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(BelowWarningThresholdLog, AccountEmail, CapturedAt);

        // Assert — 0.79 is just below the warning threshold
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        session.Severity.ShouldBe("normal");
    }

    [Fact]
    public void WhenUtilizationIsExactlyOne_SeverityIsCritical()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(ExactCriticalThresholdLog, AccountEmail, CapturedAt);

        // Assert — 1.0 is the critical threshold boundary (inclusive)
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageRateLimit session = parsed.Snapshot.Limits.Single(l => l.Kind == "session");
        session.Severity.ShouldBe("critical");
    }
}
