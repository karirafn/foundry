using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Parses the stream-json output of a real Claude CLI turn run by
/// <c>UsageReportSpec</c> into a <see cref="UsageParseResult"/>.
/// <para>
/// The data source is a minimal real turn (<c>claude -p "Reply with: ok" --max-turns 1
/// --output-format stream-json --verbose</c>). A real turn emits a <c>rate_limit_event</c>
/// that carries <c>rate_limit_info.unifiedWindows</c> — the only reliable source of
/// per-window utilization data.
/// </para>
/// <para>
/// Parser design decisions:
/// <list type="bullet">
/// <item>Pure of <c>DateTimeOffset.UtcNow</c> — <paramref name="capturedAt"/> and
///   <paramref name="accountEmail"/> are stamped by the caller (coordinator), not the parser.</item>
/// <item>Signature: <c>Parse(string log, string accountEmail, DateTimeOffset capturedAt)</c> so the
///   result is a complete, ready-to-store <see cref="UsageSnapshot"/> on success.</item>
/// <item><c>ExtraUsage</c> is always <c>null</c> — <c>rate_limit_event</c> carries no credit
///   amounts; populating credits from this source is a follow-up.</item>
/// </list>
/// </para>
/// </summary>
internal static class UsageReportParser
{
    // Window-key → Kind mapping (CLI field name → domain term).
    private const string FiveHourKey = "five_hour";
    private const string SevenDayKey = "seven_day";
    private const string SessionKind = "session";
    private const string WeeklyAllKind = "weekly_all";

    // Severity thresholds — match frontend display logic.
    private const decimal CriticalThreshold = 1.0m;
    private const decimal WarningThreshold = 0.8m;

    // Valid range for DateTimeOffset.FromUnixTimeSeconds (from the BCL documentation).
    private const long MinUnixSeconds = -62135596800L;
    private const long MaxUnixSeconds = 253402300799L;

    /// <summary>
    /// Parses a stream-json log, stamping the resulting snapshot with
    /// <paramref name="accountEmail"/> and <paramref name="capturedAt"/>.
    /// </summary>
    /// <returns>
    /// <see cref="UsageParseResult.Parsed"/> when a <c>rate_limit_event</c> line is found
    /// and <c>rate_limit_info.unifiedWindows</c> is present and parseable;
    /// <see cref="UsageParseResult.NoUsageReport"/> when no <c>rate_limit_event</c> is
    /// present (no API turn / API-key mode) or <c>unifiedWindows</c> is absent (CLI
    /// version too old to emit it);
    /// <see cref="UsageParseResult.Unparseable"/> on malformed JSON or a
    /// <c>rate_limit_info</c> with broken field values.
    /// </returns>
    public static UsageParseResult Parse(string log, string accountEmail, DateTimeOffset capturedAt)
    {
        UsageParseResult? earlyResult = null;
        JsonObject? lastRateLimitInfo = FindLastRateLimitInfo(log, out earlyResult);

        if (earlyResult is not null)
        {
            return earlyResult;
        }

        if (lastRateLimitInfo is null)
        {
            // No rate_limit_event found (or none had unifiedWindows) — no API turn happened,
            // API-key mode, or CLI too old to emit unifiedWindows.
            return new UsageParseResult.NoUsageReport();
        }

        return ParseRateLimitInfo(lastRateLimitInfo, accountEmail, capturedAt);
    }

    /// <summary>
    /// Scans all lines and returns the LAST <c>rate_limit_info</c> whose
    /// <c>unifiedWindows</c> is non-null.
    /// <para>
    /// The CLI may emit a preamble <c>rate_limit_event</c> before the real one — taking
    /// the last event with <c>unifiedWindows</c> ensures we use the most complete data.
    /// </para>
    /// </summary>
    /// <param name="log">The raw stream-json log.</param>
    /// <param name="earlyResult">
    /// Set to a non-null result when parsing must terminate early (malformed JSON or a
    /// malformed <c>rate_limit_event</c>); otherwise <c>null</c>.
    /// </param>
    private static JsonObject? FindLastRateLimitInfo(string log, out UsageParseResult? earlyResult)
    {
        earlyResult = null;
        JsonObject? last = null;

        foreach (string line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            JsonObject? node;

            try
            {
                node = JsonNode.Parse(trimmed) as JsonObject;
            }
            catch (JsonException)
            {
                earlyResult = new UsageParseResult.Unparseable();
                return null;
            }

            if (node is null)
            {
                continue;
            }

            if (node["type"]?.GetValue<string>() != "rate_limit_event")
            {
                continue;
            }

            if (node["rate_limit_info"] is not JsonObject rateLimitInfo)
            {
                // rate_limit_event present but malformed — terminate immediately.
                earlyResult = new UsageParseResult.Unparseable();
                return null;
            }

            // Only keep events that carry unifiedWindows; preamble events often omit it.
            if (rateLimitInfo["unifiedWindows"] is JsonObject)
            {
                last = rateLimitInfo;
            }
        }

        return last;
    }

    private static UsageParseResult ParseRateLimitInfo(
        JsonObject rateLimitInfo,
        string accountEmail,
        DateTimeOffset capturedAt)
    {
        try
        {
            // unifiedWindows presence is guaranteed by FindLastRateLimitInfo.
            JsonObject unifiedWindows = (JsonObject)rateLimitInfo["unifiedWindows"]!;

            string? rateLimitType = rateLimitInfo["rateLimitType"]?.GetValue<string>();

            List<UsageRateLimit> limits = [];

            foreach ((string windowKey, string kind) in WindowMappings())
            {
                if (unifiedWindows[windowKey] is not JsonObject windowObj)
                {
                    continue;
                }

                // Absent utilization is a malformed window — return Unparseable rather than
                // silently defaulting to 0, which would misrepresent the account's usage.
                if (windowObj["utilization"] is not JsonNode utilizationNode)
                {
                    return new UsageParseResult.Unparseable();
                }

                decimal utilization = utilizationNode.GetValue<decimal>();

                // Absent or out-of-range resetsAt is a malformed window — return Unparseable
                // rather than silently defaulting to epoch or throwing ArgumentOutOfRangeException.
                if (windowObj["resetsAt"] is not JsonNode resetsAtNode)
                {
                    return new UsageParseResult.Unparseable();
                }

                long resetsAtUnix = resetsAtNode.GetValue<long>();

                if (resetsAtUnix < MinUnixSeconds || resetsAtUnix > MaxUnixSeconds)
                {
                    return new UsageParseResult.Unparseable();
                }

                DateTimeOffset resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetsAtUnix);
                decimal percent = utilization * 100m;

                // Server-side severity mapping — the frontend clamps the bar; the parser derives severity.
                string severity = utilization >= CriticalThreshold
                    ? "critical"
                    : utilization >= WarningThreshold
                        ? "warning"
                        : "normal";

                // IsActive: true for the window whose key matches rateLimitType.
                // When rateLimitType is null or unknown, the highest-utilization fallback below applies.
                bool isActive = rateLimitType is not null &&
                    MapRateLimitTypeToWindowKey(rateLimitType) == windowKey;

                limits.Add(new UsageRateLimit(
                    Kind: kind,
                    Percent: percent,
                    ResetsAt: resetsAt,
                    ModelDisplayName: null,
                    Severity: severity,
                    IsActive: isActive));
            }

            // When rateLimitType is null, or did not match any known window key, mark the window
            // with the highest utilization as active. This covers both absent rateLimitType and
            // any future divergence between rateLimitType values and window key names.
            if (limits.All(l => !l.IsActive) && limits.Count > 0)
            {
                decimal maxUtil = limits.Max(l => l.Percent);
                limits = limits
                    .Select(l => l with { IsActive = l.Percent == maxUtil })
                    .ToList();
            }

            // ExtraUsage is always null — rate_limit_event carries no credit amounts;
            // populating credits from this source is a follow-up.
            UsageSnapshot snapshot = new(
                Limits: limits,
                ExtraUsage: null,
                CapturedAt: capturedAt,
                AccountEmail: accountEmail,
                LastRefreshFailedAt: null);

            return new UsageParseResult.Parsed(snapshot);
        }
        catch (InvalidOperationException)
        {
            // Token-type mismatch (e.g. string where number expected) — treat as Unparseable.
            return new UsageParseResult.Unparseable();
        }
        catch (OverflowException)
        {
            // Arithmetic overflow (e.g. utilization value too large for decimal multiplication).
            return new UsageParseResult.Unparseable();
        }
    }

    private static IEnumerable<(string Key, string Kind)> WindowMappings()
    {
        yield return (FiveHourKey, SessionKind);
        yield return (SevenDayKey, WeeklyAllKind);
    }

    /// <summary>
    /// Maps a <c>rateLimitType</c> value to the corresponding <c>unifiedWindows</c> key.
    /// Currently <c>rateLimitType</c> shares the same value space as the window keys
    /// ("five_hour", "seven_day"), so this is an identity map. The highest-utilization
    /// fallback in <see cref="ParseRateLimitInfo"/> covers any future divergence.
    /// </summary>
    private static string? MapRateLimitTypeToWindowKey(string rateLimitType) =>
        rateLimitType switch
        {
            FiveHourKey => FiveHourKey,
            SevenDayKey => SevenDayKey,
            _ => null,
        };
}
