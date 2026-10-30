using Foundry.Modules.Credentials.Features.Usage;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsageReportParserTests;

public sealed class Parse
{
    private const string AccountEmail = "user@example.com";
    private static readonly DateTimeOffset CapturedAt = new(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Full stream-json log from <c>claude -p "/usage" --output-format stream-json --verbose</c>
    /// (CLI 2.1.302). Contains session + weekly_all + two weekly_scoped limits plus extra_usage.
    /// </summary>
    private const string FullUsageLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-5","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":""}],"model":"claude-opus-4-5-synthetic","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":0,"output_tokens":0}},"usage_report":{"session":{"tokens_used":12345,"tokens_limit":88000},"rate_limits":{"limits":[{"kind":"session","group":"tokens","percent":0.14,"resets_at":"2025-01-15T15:30:00.000Z","scope":{"type":"session"},"severity":"normal","is_active":false},{"kind":"weekly_all","group":"tokens","percent":0.65,"resets_at":"2025-01-20T00:00:00.000Z","scope":{"type":"weekly_all"},"severity":"warning","is_active":true},{"kind":"weekly_scoped","group":"tokens","percent":0.30,"resets_at":"2025-01-20T00:00:00.000Z","scope":{"type":"weekly_scoped","model":{"id":"claude-opus-4-5","display_name":"Claude Opus 4.5"}},"severity":"normal","is_active":false},{"kind":"weekly_scoped","group":"tokens","percent":0.80,"resets_at":"2025-01-20T00:00:00.000Z","scope":{"type":"weekly_scoped","model":{"id":"claude-sonnet-4-5","display_name":"Claude Sonnet 4.5"}},"severity":"critical","is_active":false}],"extra_usage":{"is_enabled":true,"monthly_limit":50000,"used_credits":16568,"utilization":0.33,"currency":"USD"}}}}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":123,"duration_api_ms":100,"num_turns":1,"result":"","total_cost_usd":0,"usage":{"input_tokens":0,"output_tokens":0,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"session_id":"abc123"}
        """;

    /// <summary>A stream-json log where no assistant message carries a usage_report.</summary>
    private const string NoUsageReportLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-5","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"Some response"}],"model":"claude-opus-4-5","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":5}}}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":500,"duration_api_ms":400,"num_turns":1,"result":"Some response","total_cost_usd":0.001,"usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"session_id":"abc123"}
        """;

    /// <summary>A log where usage_report.rate_limits is null (usage-based billing).</summary>
    private const string NullRateLimitsLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-5","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":""}],"model":"claude-opus-4-5-synthetic","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":0,"output_tokens":0}},"usage_report":{"session":{"tokens_used":0,"tokens_limit":0},"rate_limits":null}}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"duration_api_ms":80,"num_turns":1,"result":"","total_cost_usd":0,"usage":{"input_tokens":0,"output_tokens":0,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"session_id":"abc123"}
        """;

    /// <summary>A log with malformed JSON on one of the lines.</summary>
    private const string MalformedJsonLog = """
        {"type":"system","subtype":"init"}
        {INVALID JSON CANNOT PARSE THIS
        """;

    /// <summary>A log where usage_report is present but missing the required rate_limits.limits field.</summary>
    private const string MissingRequiredFieldLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-5","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":""}],"model":"claude-opus-4-5-synthetic","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":0,"output_tokens":0}},"usage_report":{"session":{"tokens_used":0,"tokens_limit":0},"rate_limits":{"extra_usage":{"is_enabled":true,"monthly_limit":50000,"used_credits":16568,"utilization":0.33,"currency":"USD"}}}}
        """;

    /// <summary>A log where extra_usage is absent from the rate_limits object.</summary>
    private const string NoExtraUsageLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-5","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":""}],"model":"claude-opus-4-5-synthetic","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":0,"output_tokens":0}},"usage_report":{"session":{"tokens_used":5000,"tokens_limit":88000},"rate_limits":{"limits":[{"kind":"session","group":"tokens","percent":0.06,"resets_at":"2025-01-15T15:30:00.000Z","scope":{"type":"session"},"severity":"normal","is_active":false}]}}}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":100,"duration_api_ms":80,"num_turns":1,"result":"","total_cost_usd":0,"usage":{"input_tokens":0,"output_tokens":0,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"session_id":"abc123"}
        """;

    [Fact]
    public void WhenFullUsageReport_ReturnsParsedWithAllLimitsAndExtraUsage()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullUsageLog, AccountEmail, CapturedAt);

        // Assert
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        UsageSnapshot snapshot = parsed.Snapshot;

        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.AccountEmail.ShouldBe(AccountEmail),
            () => snapshot.CapturedAt.ShouldBe(CapturedAt),
            () => snapshot.LastRefreshFailedAt.ShouldBeNull(),
            () => snapshot.Limits.Count.ShouldBe(4));

        UsageRateLimit session = snapshot.Limits.Single(l => l.Kind == "session");
        session.ShouldSatisfyAllConditions(
            () => session.Percent.ShouldBe(0.14m),
            () => session.ResetsAt.ShouldBe(new DateTimeOffset(2025, 1, 15, 15, 30, 0, TimeSpan.Zero)),
            () => session.ModelDisplayName.ShouldBeNull(),
            () => session.Severity.ShouldBe("normal"),
            () => session.IsActive.ShouldBeFalse());

        UsageRateLimit weeklyAll = snapshot.Limits.Single(l => l.Kind == "weekly_all");
        weeklyAll.ShouldSatisfyAllConditions(
            () => weeklyAll.Percent.ShouldBe(0.65m),
            () => weeklyAll.Severity.ShouldBe("warning"),
            () => weeklyAll.IsActive.ShouldBeTrue(),
            () => weeklyAll.ModelDisplayName.ShouldBeNull());

        UsageExtraCredits extra = snapshot.ExtraUsage.ShouldNotBeNull();
        extra.ShouldSatisfyAllConditions(
            () => extra.IsEnabled.ShouldBeTrue(),
            () => extra.MonthlyLimit.ShouldBe(50000L),
            () => extra.UsedCredits.ShouldBe(16568L),
            () => extra.Utilization.ShouldBe(0.33m),
            () => extra.Currency.ShouldBe("USD"));
    }

    [Fact]
    public void WhenNoUsageReportInLog_ReturnsNoUsageReport()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(NoUsageReportLog, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.NoUsageReport>();
    }

    [Fact]
    public void WhenRateLimitsIsNull_ReturnsNoUsageReport()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(NullRateLimitsLog, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.NoUsageReport>();
    }

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
    public void WhenUsageReportMissingRequiredField_ReturnsUnparseable()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(MissingRequiredFieldLog, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    [Fact]
    public void WhenMultipleWeeklyScopedEntries_MapsEachWithModelDisplayName()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(FullUsageLog, AccountEmail, CapturedAt);

        // Assert
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        IReadOnlyList<UsageRateLimit> scoped = parsed.Snapshot.Limits
            .Where(l => l.Kind == "weekly_scoped")
            .ToList();

        scoped.Count.ShouldBe(2);
        scoped.ShouldContain(l =>
            l.ModelDisplayName == "Claude Opus 4.5" &&
            l.Percent == 0.30m &&
            l.Severity == "normal");
        scoped.ShouldContain(l =>
            l.ModelDisplayName == "Claude Sonnet 4.5" &&
            l.Percent == 0.80m &&
            l.Severity == "critical");
    }

    [Fact]
    public void WhenExtraUsageAbsent_SnapshotExtraUsageIsNull()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(NoExtraUsageLog, AccountEmail, CapturedAt);

        // Assert
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        parsed.Snapshot.ExtraUsage.ShouldBeNull();
    }

    /// <summary>
    /// A log where a limit field has a wrong token type (string where number expected).
    /// The CLI could emit unexpected types; the parser must not throw — return Unparseable.
    /// </summary>
    private const string TypeMismatchedLimitLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-5","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":""}],"model":"claude-opus-4-5-synthetic","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":0,"output_tokens":0}},"usage_report":{"session":{"tokens_used":0,"tokens_limit":0},"rate_limits":{"limits":[{"kind":"session","group":"tokens","percent":"high","resets_at":"2025-01-15T15:30:00.000Z","scope":{"type":"session"},"severity":"normal","is_active":false}]}}}
        """;

    /// <summary>
    /// A log where rate_limits.limits is an empty array — no limits entries present.
    /// </summary>
    private const string EmptyLimitsArrayLog = """
        {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-5","permissionMode":"default","apiKeySource":"none"}
        {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":""}],"model":"claude-opus-4-5-synthetic","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":0,"output_tokens":0}},"usage_report":{"session":{"tokens_used":0,"tokens_limit":0},"rate_limits":{"limits":[]}}}
        """;

    [Fact]
    public void WhenLimitFieldHasWrongTokenType_ReturnsUnparseable()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(TypeMismatchedLimitLog, AccountEmail, CapturedAt);

        // Assert — type mismatch must not throw; it must produce Unparseable
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    [Fact]
    public void WhenLimitsArrayIsEmpty_ReturnsParsedWithNoLimits()
    {
        // Arrange
        // Act
        UsageParseResult result = UsageReportParser.Parse(EmptyLimitsArrayLog, AccountEmail, CapturedAt);

        // Assert — empty limits array is valid; snapshot is Parsed with zero limits
        UsageParseResult.Parsed parsed = result.ShouldBeOfType<UsageParseResult.Parsed>();
        parsed.Snapshot.Limits.ShouldBeEmpty();
    }
}
