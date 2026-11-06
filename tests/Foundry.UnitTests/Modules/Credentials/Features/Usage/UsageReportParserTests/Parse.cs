using Foundry.Modules.Credentials.Features.Usage;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsageReportParserTests;

/// <summary>
/// Tests for <see cref="UsageReportParser"/>, which reads the <c>usage_report</c> carried on the
/// synthetic assistant message of <c>claude -p "/usage" --output-format stream-json --verbose</c>.
/// Fixture shape verified against Claude Code CLI 2.1.316.
/// </summary>
public sealed class Parse
{
    private const string AccountEmail = "user@example.com";
    private static readonly DateTimeOffset CapturedAt = new(2026, 11, 6, 10, 30, 0, TimeSpan.Zero);

    private const string InitLine =
        """{"type":"system","subtype":"init","cwd":"/workspace","session_id":"s1","tools":[],"mcp_servers":[],"model":"claude-opus-4-8","permissionMode":"default","apiKeySource":"none","claude_code_version":"2.1.316"}""";

    private const string ResultLine =
        """{"type":"result","subtype":"success","is_error":false,"duration_ms":900,"duration_api_ms":0,"num_turns":0,"result":"","total_cost_usd":0,"session_id":"s1"}""";

    private const string Session =
        """{"total_cost_usd":0,"total_api_duration_ms":0,"total_duration_ms":0,"total_lines_added":0,"total_lines_removed":0,"model_usage":{}}""";

    private const string FullLimits = """
        [
          {"kind":"session","group":"session","percent":14,"resets_at":"2026-11-06T13:00:00Z","scope":null,"severity":"normal","is_active":false},
          {"kind":"weekly_all","group":"weekly","percent":65.5,"resets_at":"2026-11-10T09:00:00+00:00","severity":"warning","is_active":true},
          {"kind":"weekly_scoped","group":"weekly","percent":51,"resets_at":"2026-11-10T09:00:00Z","scope":{"model":{"display_name":"Opus"}},"severity":"normal","is_active":false}
        ]
        """;

    private const string FullExtraUsage =
        """{"is_enabled":true,"monthly_limit":50000,"used_credits":16568,"utilization":null,"currency":"USD"}""";

    private static string AssistantLine(string usageReportJson) =>
        """{"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","model":"<synthetic>","content":[{"type":"text","text":"Current session ..."}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}},"parent_tool_use_id":null,"session_id":"s1","usage_report":"""
        + Compact(usageReportJson) + "}";

    private static string RateLimitsReport(string limitsJson, string extraUsageJson) =>
        """{"session":""" + Session + ""","rate_limits":{"limits":""" + Compact(limitsJson)
        + ""","extra_usage":""" + extraUsageJson + "}}";

    private static string Compact(string json) => string.Join(' ', json.Split('\n', StringSplitOptions.TrimEntries));

    private static string Log(params string[] lines) => string.Join('\n', lines);

    private static string ReportLog(string limitsJson = FullLimits, string extraUsageJson = FullExtraUsage) =>
        Log(InitLine, AssistantLine(RateLimitsReport(limitsJson, extraUsageJson)), ResultLine);

    private static UsageSnapshot ParsedSnapshot(string log) =>
        UsageReportParser.Parse(log, AccountEmail, CapturedAt)
            .ShouldBeOfType<UsageParseResult.Parsed>()
            .Snapshot;

    // ---------------------------------------------------------------------------
    // Parsed
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenReportCarriesRateLimits_ReturnsParsedSnapshotStampedWithSourceAndCaller()
    {
        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog());

        // Assert
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Source.ShouldBe(UsageSnapshotSource.UsageReport),
            () => snapshot.AccountEmail.ShouldBe(AccountEmail),
            () => snapshot.CapturedAt.ShouldBe(CapturedAt),
            () => snapshot.LastRefreshFailedAt.ShouldBeNull(),
            () => snapshot.Limits.Select(l => l.Kind).ShouldBe(["session", "weekly_all", "weekly_scoped"]));
    }

    [Fact]
    public void WhenReportCarriesRateLimits_MapsEachLimitVerbatim()
    {
        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog());

        // Assert
        UsageRateLimit weekly = snapshot.Limits[1];
        UsageRateLimit scoped = snapshot.Limits[2];
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Limits[0].Percent.ShouldBe(14m),
            () => snapshot.Limits[0].ResetsAt.ShouldBe(new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero)),
            () => snapshot.Limits[0].ModelDisplayName.ShouldBeNull(),
            () => snapshot.Limits[0].IsActive.ShouldBeFalse(),
            () => weekly.Percent.ShouldBe(65.5m),
            () => weekly.Severity.ShouldBe("warning"),
            () => weekly.IsActive.ShouldBeTrue(),
            () => weekly.ModelDisplayName.ShouldBeNull(),
            () => scoped.ModelDisplayName.ShouldBe("Opus"),
            () => scoped.ResetsAt.ShouldBe(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void WhenSeverityIsUnknown_PassesItThrough()
    {
        // Arrange
        string limits = """[{"kind":"session","group":"session","percent":50,"resets_at":"2026-11-06T13:00:00Z","severity":"extreme","is_active":true}]""";

        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog(limits));

        // Assert
        snapshot.Limits.ShouldHaveSingleItem().Severity.ShouldBe("extreme");
    }

    [Fact]
    public void WhenResetsAtIsNull_LimitHasNoResetTime()
    {
        // Arrange
        string limits = """[{"kind":"session","group":"session","percent":0,"resets_at":null,"severity":"normal","is_active":true}]""";

        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog(limits));

        // Assert
        snapshot.Limits.ShouldHaveSingleItem().ResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenLimitsIsEmpty_ReturnsParsedWithNoLimits()
    {
        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog("[]"));

        // Assert
        snapshot.Limits.ShouldBeEmpty();
    }

    [Fact]
    public void WhenExtraUsagePresent_MapsCredits()
    {
        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog());

        // Assert
        UsageExtraCredits credits = snapshot.ExtraUsage.ShouldNotBeNull();
        credits.ShouldBe(new UsageExtraCredits(
            IsEnabled: true,
            MonthlyLimit: 50000,
            UsedCredits: 16568,
            Utilization: null,
            Currency: "USD"));
    }

    [Fact]
    public void WhenExtraUsageCarriesUtilizationAndNullLimit_MapsThemAsNullable()
    {
        // Arrange
        string extra = """{"is_enabled":true,"monthly_limit":null,"used_credits":1200,"utilization":12.5,"currency":"EUR"}""";

        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog(extraUsageJson: extra));

        // Assert
        UsageExtraCredits credits = snapshot.ExtraUsage.ShouldNotBeNull();
        credits.ShouldSatisfyAllConditions(
            () => credits.MonthlyLimit.ShouldBeNull(),
            () => credits.Utilization.ShouldBe(12.5m),
            () => credits.Currency.ShouldBe("EUR"));
    }

    [Fact]
    public void WhenExtraUsageOmitsCurrencyAndUsedCredits_DefaultsToUsdAndZero()
    {
        // Arrange — currency is optional and used_credits nullable in the CLI schema
        string extra = """{"is_enabled":true,"monthly_limit":50000,"used_credits":null,"utilization":null}""";

        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog(extraUsageJson: extra));

        // Assert
        UsageExtraCredits credits = snapshot.ExtraUsage.ShouldNotBeNull();
        credits.ShouldSatisfyAllConditions(
            () => credits.Currency.ShouldBe("USD"),
            () => credits.UsedCredits.ShouldBe(0));
    }

    [Fact]
    public void WhenUsedCreditsIsFractional_TruncatesToWholeMinorUnits()
    {
        // Arrange
        string extra = """{"is_enabled":true,"monthly_limit":50000,"used_credits":16568.7,"utilization":null,"currency":"USD"}""";

        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog(extraUsageJson: extra));

        // Assert
        snapshot.ExtraUsage.ShouldNotBeNull().UsedCredits.ShouldBe(16568);
    }

    [Fact]
    public void WhenExtraUsageIsNull_HasNoCredits()
    {
        // Act
        UsageSnapshot snapshot = ParsedSnapshot(ReportLog(extraUsageJson: "null"));

        // Assert
        snapshot.ExtraUsage.ShouldBeNull();
    }

    [Fact]
    public void WhenOutputInterleavesNonJsonLines_SkipsThem()
    {
        // Arrange
        string log = Log(
            "Warning: something on stderr",
            InitLine,
            AssistantLine(RateLimitsReport(FullLimits, FullExtraUsage)),
            """{"type":"result","subtype":"succ""");

        // Act
        UsageSnapshot snapshot = ParsedSnapshot(log);

        // Assert
        snapshot.Limits.Count.ShouldBe(3);
    }

    // ---------------------------------------------------------------------------
    // RateLimitsMissing
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenRateLimitsIsNull_ReturnsRateLimitsMissingWithoutCredits()
    {
        // Arrange
        string log = Log(InitLine, AssistantLine($$"""{"session":{{Session}},"rate_limits":null}"""), ResultLine);

        // Act
        UsageParseResult result = UsageReportParser.Parse(log, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.RateLimitsMissing>().ExtraCredits.ShouldBeNull();
    }

    [Fact]
    public void WhenRateLimitsIsAbsent_ReturnsRateLimitsMissingWithoutCredits()
    {
        // Arrange
        string log = Log(InitLine, AssistantLine($$"""{"session":{{Session}}}"""), ResultLine);

        // Act
        UsageParseResult result = UsageReportParser.Parse(log, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.RateLimitsMissing>().ExtraCredits.ShouldBeNull();
    }

    [Fact]
    public void WhenLimitsIsNullButExtraUsagePresent_ReturnsRateLimitsMissingCarryingCredits()
    {
        // Arrange
        string log = Log(
            InitLine,
            AssistantLine("""{"session":""" + Session + ""","rate_limits":{"limits":null,"extra_usage":""" + FullExtraUsage + "}}"),
            ResultLine);

        // Act
        UsageParseResult result = UsageReportParser.Parse(log, AccountEmail, CapturedAt);

        // Assert
        UsageExtraCredits credits = result.ShouldBeOfType<UsageParseResult.RateLimitsMissing>().ExtraCredits.ShouldNotBeNull();
        credits.MonthlyLimit.ShouldBe(50000);
    }

    // ---------------------------------------------------------------------------
    // NoUsageReport
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenNoLineCarriesUsageReport_ReturnsNoUsageReport()
    {
        // Arrange — API-key mode: the CLI answers /usage without a structured twin
        string log = Log(
            InitLine,
            """{"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","model":"<synthetic>","content":[{"type":"text","text":"/usage is only available for subscription plans."}]},"parent_tool_use_id":null,"session_id":"s1"}""",
            ResultLine);

        // Act
        UsageParseResult result = UsageReportParser.Parse(log, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.NoUsageReport>();
    }

    // ---------------------------------------------------------------------------
    // Unparseable
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("Invalid API key · Please run /login")]
    [InlineData("{INVALID JSON CANNOT PARSE THIS")]
    public void WhenOutputHasNoJsonLine_ReturnsUnparseable(string log)
    {
        // Act
        UsageParseResult result = UsageReportParser.Parse(log, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    [Theory]
    [InlineData("""[{"kind":"session","percent":"lots","resets_at":"2026-11-06T13:00:00Z","severity":"normal","is_active":true}]""")]
    [InlineData("""[{"kind":"session","percent":10,"resets_at":"not-a-date","severity":"normal","is_active":true}]""")]
    [InlineData("""[{"percent":10,"resets_at":"2026-11-06T13:00:00Z","severity":"normal","is_active":true}]""")]
    [InlineData("""[{"kind":"session","resets_at":"2026-11-06T13:00:00Z","severity":"normal","is_active":true}]""")]
    [InlineData("""[{"kind":"session","percent":10,"resets_at":"2026-11-06T13:00:00Z","is_active":true}]""")]
    [InlineData("""[{"kind":"session","percent":10,"resets_at":"2026-11-06T13:00:00Z","severity":"normal"}]""")]
    [InlineData("""[{"kind":"session","percent":10,"resets_at":"2026-11-06T13:00:00Z","severity":"normal","is_active":"yes"}]""")]
    [InlineData("""["session"]""")]
    [InlineData("""{"kind":"session"}""")]
    public void WhenALimitIsMalformed_ReturnsUnparseable(string limits)
    {
        // Act
        UsageParseResult result = UsageReportParser.Parse(ReportLog(limits), AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    [Theory]
    [InlineData("""{"is_enabled":"yes","monthly_limit":50000,"used_credits":1,"utilization":null,"currency":"USD"}""")]
    [InlineData("""{"monthly_limit":50000,"used_credits":1,"utilization":null,"currency":"USD"}""")]
    [InlineData("""{"is_enabled":true,"monthly_limit":"lots","used_credits":1,"utilization":null,"currency":"USD"}""")]
    [InlineData("""[1]""")]
    public void WhenExtraUsageIsMalformed_ReturnsUnparseable(string extra)
    {
        // Act
        UsageParseResult result = UsageReportParser.Parse(ReportLog(extraUsageJson: extra), AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("{\"session\":{},\"rate_limits\":\"none\"}")]
    public void WhenUsageReportOrRateLimitsIsNotAnObject_ReturnsUnparseable(string usageReport)
    {
        // Arrange
        string log = Log(InitLine, AssistantLine(usageReport), ResultLine);

        // Act
        UsageParseResult result = UsageReportParser.Parse(log, AccountEmail, CapturedAt);

        // Assert
        result.ShouldBeOfType<UsageParseResult.Unparseable>();
    }

    // ---------------------------------------------------------------------------
    // ReadClaudeCodeVersion
    // ---------------------------------------------------------------------------

    [Fact]
    public void ReadClaudeCodeVersion_WhenInitLineCarriesVersion_ReturnsIt()
    {
        // Act
        string? version = UsageReportParser.ReadClaudeCodeVersion(ReportLog());

        // Assert
        version.ShouldBe("2.1.316");
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"type":"system","subtype":"init","session_id":"s1"}""")]
    [InlineData("""{"type":"system","subtype":"init","claude_code_version":42}""")]
    public void ReadClaudeCodeVersion_WhenNoVersionIsReported_ReturnsNull(string log)
    {
        // Act
        string? version = UsageReportParser.ReadClaudeCodeVersion(log);

        // Assert
        version.ShouldBeNull();
    }
}
