using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Parses the stream-json output of <c>claude -p "/usage" --output-format stream-json --verbose</c>
/// into a <see cref="UsageParseResult"/>.
/// <para>
/// The CLI answers <c>/usage</c> with a synthetic assistant message whose wrapper-level
/// <c>usage_report</c> sibling carries <c>rate_limits.limits[]</c> (Anthropic's rows, percent on a
/// 0–100 scale, ISO-8601 <c>resets_at</c>, and Anthropic's own <c>severity</c>) and
/// <c>rate_limits.extra_usage</c> (credit spend in minor units). Rows are mapped verbatim — Foundry
/// grades nothing here. The shape is documented by the CLI as experimental; a change surfaces as
/// <see cref="UsageParseResult.Unparseable"/> (a failed poll), never as wrong numbers.
/// </para>
/// <para>
/// Pure of <c>DateTimeOffset.UtcNow</c>: <c>accountEmail</c> and <c>capturedAt</c> are stamped by
/// the caller.
/// </para>
/// </summary>
internal static class UsageReportParser
{
    /// <summary>The CLI defaults a missing <c>extra_usage.currency</c> to USD; so does Foundry.</summary>
    internal const string DefaultCurrency = "USD";

    /// <returns>
    /// <see cref="UsageParseResult.Parsed"/> when the last <c>usage_report</c> carries
    /// <c>rate_limits.limits</c>;
    /// <see cref="UsageParseResult.RateLimitsMissing"/> when it carries a null or absent
    /// <c>rate_limits</c> or <c>limits</c> (with any <c>extra_usage</c> credits it still reports);
    /// <see cref="UsageParseResult.NoUsageReport"/> when the output holds JSON lines but none carries a
    /// <c>usage_report</c> (API-key mode / not reportable);
    /// <see cref="UsageParseResult.Unparseable"/> when the output holds no JSON line at all (auth-error
    /// text, empty output from a <c>timeout</c> kill) or the report has missing or broken fields.
    /// Non-JSON lines (stderr notices, a line cut by the log-length cap) are skipped.
    /// </returns>
    public static UsageParseResult Parse(string log, string accountEmail, DateTimeOffset capturedAt)
    {
        bool sawJsonLine = false;
        JsonNode? usageReport = null;
        bool sawUsageReport = false;

        foreach (JsonObject line in JsonLines(log))
        {
            sawJsonLine = true;

            if (line.TryGetPropertyValue("usage_report", out JsonNode? report))
            {
                usageReport = report;
                sawUsageReport = true;
            }
        }

        if (!sawJsonLine)
        {
            return new UsageParseResult.Unparseable();
        }

        if (!sawUsageReport)
        {
            return new UsageParseResult.NoUsageReport();
        }

        try
        {
            return ParseUsageReport(usageReport, accountEmail, capturedAt);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            // Token-type mismatch (string where a number is expected, etc.) or an unparseable value.
            return new UsageParseResult.Unparseable();
        }
    }

    /// <summary>
    /// Reads the Claude Code CLI version from the stream-json <c>system</c>/<c>init</c> line
    /// (<c>claude_code_version</c>), or <see langword="null"/> when the output reports none.
    /// </summary>
    public static string? ReadClaudeCodeVersion(string log)
    {
        foreach (JsonObject line in JsonLines(log))
        {
            if (line["type"] is JsonValue type
                && type.TryGetValue(out string? typeName)
                && typeName == "system"
                && line["claude_code_version"] is JsonValue version
                && version.TryGetValue(out string? value))
            {
                return value;
            }
        }

        return null;
    }

    private static IEnumerable<JsonObject> JsonLines(string log)
    {
        foreach (string line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line[0] != '{')
            {
                continue;
            }

            JsonObject? node;
            try
            {
                node = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }

            if (node is not null)
            {
                yield return node;
            }
        }
    }

    private static UsageParseResult ParseUsageReport(
        JsonNode? usageReport,
        string accountEmail,
        DateTimeOffset capturedAt)
    {
        if (usageReport is not JsonObject report)
        {
            return new UsageParseResult.Unparseable();
        }

        JsonNode? rateLimitsNode = report["rate_limits"];

        if (rateLimitsNode is null)
        {
            return new UsageParseResult.RateLimitsMissing(ExtraCredits: null);
        }

        if (rateLimitsNode is not JsonObject rateLimits)
        {
            return new UsageParseResult.Unparseable();
        }

        UsageExtraCredits? extraCredits = ParseExtraUsage(rateLimits["extra_usage"]);

        if (rateLimits["limits"] is not { } limitsNode)
        {
            return new UsageParseResult.RateLimitsMissing(extraCredits);
        }

        if (limitsNode is not JsonArray limitsArray)
        {
            return new UsageParseResult.Unparseable();
        }

        List<UsageRateLimit> limits = [];

        foreach (JsonNode? entry in limitsArray)
        {
            if (entry is not JsonObject limit
                || RequiredString(limit, "kind") is not { } kind
                || RequiredString(limit, "severity") is not { } severity
                || limit["percent"] is not JsonValue percent
                || limit["is_active"] is not JsonValue isActive)
            {
                return new UsageParseResult.Unparseable();
            }

            limits.Add(new UsageRateLimit(
                Kind: kind,
                Percent: percent.GetValue<decimal>(),
                ResetsAt: ParseResetsAt(limit["resets_at"]),
                ModelDisplayName: limit["scope"]?["model"]?["display_name"]?.GetValue<string>(),
                Severity: severity,
                IsActive: isActive.GetValue<bool>()));
        }

        UsageSnapshot snapshot = new(
            Limits: limits,
            ExtraUsage: extraCredits,
            CapturedAt: capturedAt,
            AccountEmail: accountEmail,
            LastRefreshFailedAt: null,
            Source: UsageSnapshotSource.UsageReport);

        return new UsageParseResult.Parsed(snapshot);
    }

    private static string? RequiredString(JsonObject node, string property) =>
        node[property] is JsonValue value ? value.GetValue<string>() : null;

    private static DateTimeOffset? ParseResetsAt(JsonNode? node) =>
        node is null
            ? null
            : DateTimeOffset.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static UsageExtraCredits? ParseExtraUsage(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonObject extra || extra["is_enabled"] is not JsonValue isEnabled)
        {
            throw new InvalidOperationException("extra_usage is malformed.");
        }

        decimal? monthlyLimit = extra["monthly_limit"]?.GetValue<decimal>();
        decimal? usedCredits = extra["used_credits"]?.GetValue<decimal>();

        return new UsageExtraCredits(
            IsEnabled: isEnabled.GetValue<bool>(),
            MonthlyLimit: monthlyLimit is { } limit ? (long)decimal.Truncate(limit) : null,
            UsedCredits: usedCredits is { } used ? (long)decimal.Truncate(used) : 0,
            Utilization: extra["utilization"]?.GetValue<decimal>(),
            Currency: extra["currency"]?.GetValue<string>() ?? DefaultCurrency);
    }
}
