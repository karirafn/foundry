using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Parses the stream-json output of a real Claude CLI turn run by
/// <c>UsageReportSpec</c> into a <see cref="UsageParseResult"/>.
/// <para>
/// The data source is a minimal real turn (<c>claude -p "Reply with: ok" --max-turns 1
/// --output-format stream-json --verbose</c>) rather than the <c>/usage</c> print-mode
/// command. A real turn emits a <c>rate_limit_event</c> that carries
/// <c>rate_limit_info.unifiedWindows</c> — the only reliable source of per-window
/// utilization data.
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
                return new UsageParseResult.Unparseable();
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
                // rate_limit_event present but malformed — treat as Unparseable.
                return new UsageParseResult.Unparseable();
            }

            return ParseRateLimitInfo(rateLimitInfo, accountEmail, capturedAt);
        }

        // No rate_limit_event found — no API turn happened or API-key mode.
        return new UsageParseResult.NoUsageReport();
    }

    private static UsageParseResult ParseRateLimitInfo(
        JsonObject rateLimitInfo,
        string accountEmail,
        DateTimeOffset capturedAt)
    {
        try
        {
            // unifiedWindows absent means CLI < 2.1.302 — return NoUsageReport (not a failure).
            if (rateLimitInfo["unifiedWindows"] is not JsonObject unifiedWindows)
            {
                return new UsageParseResult.NoUsageReport();
            }

            string? rateLimitType = rateLimitInfo["rateLimitType"]?.GetValue<string>();

            List<UsageRateLimit> limits = [];

            foreach ((string windowKey, string kind) in WindowMappings())
            {
                if (unifiedWindows[windowKey] is not JsonObject windowObj)
                {
                    continue;
                }

                decimal utilization = windowObj["utilization"]?.GetValue<decimal>() ?? 0m;
                long resetsAtUnix = windowObj["resetsAt"]?.GetValue<long>() ?? 0L;
                DateTimeOffset resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetsAtUnix);
                decimal percent = utilization * 100m;

                // Server-side severity mapping — the frontend clamps the bar; the parser derives severity.
                string severity = utilization >= 1.0m
                    ? "critical"
                    : utilization >= 0.8m
                        ? "warning"
                        : "normal";

                // IsActive: true for the window whose key matches rateLimitType.
                // Resolved after all windows are collected when rateLimitType is unknown.
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

            // When rateLimitType did not match any known window key, mark the window
            // with the highest utilization as active.
            if (rateLimitType is not null &&
                limits.All(l => !l.IsActive) &&
                limits.Count > 0)
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
    }

    private static IEnumerable<(string Key, string Kind)> WindowMappings()
    {
        yield return (FiveHourKey, SessionKind);
        yield return (SevenDayKey, WeeklyAllKind);
    }

    private static string? MapRateLimitTypeToWindowKey(string rateLimitType) =>
        rateLimitType switch
        {
            FiveHourKey => FiveHourKey,
            SevenDayKey => SevenDayKey,
            _ => null,
        };
}
