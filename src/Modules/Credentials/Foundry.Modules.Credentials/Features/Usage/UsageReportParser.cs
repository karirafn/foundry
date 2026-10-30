using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Parses the stream-json output of <c>claude -p "/usage" --output-format stream-json --verbose</c>
/// into a <see cref="UsageParseResult"/>.
/// <para>
/// Parser design decisions:
/// <list type="bullet">
/// <item>Pure of <c>DateTimeOffset.UtcNow</c> — <paramref name="capturedAt"/> and
///   <paramref name="accountEmail"/> are stamped by the caller (coordinator), not the parser.</item>
/// <item>Signature: <c>Parse(string log, string accountEmail, DateTimeOffset capturedAt)</c> so the
///   result is a complete, ready-to-store <see cref="UsageSnapshot"/> on success. The coordinator
///   (step 6) passes the account email it already loaded from the DB and the timestamp it stamps.</item>
/// </list>
/// </para>
/// </summary>
internal static class UsageReportParser
{
    /// <summary>
    /// Parses a stream-json log, stamping the resulting snapshot with
    /// <paramref name="accountEmail"/> and <paramref name="capturedAt"/>.
    /// </summary>
    /// <returns>
    /// <see cref="UsageParseResult.Parsed"/> when <c>usage_report</c> is found and valid;
    /// <see cref="UsageParseResult.NoUsageReport"/> when the log emits no <c>usage_report</c>
    /// or <c>rate_limits</c> is null (usage-based billing / API-key mode);
    /// <see cref="UsageParseResult.Unparseable"/> on malformed JSON or missing required fields.
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

            // Only assistant messages can carry usage_report.
            if (node["type"]?.GetValue<string>() != "assistant")
            {
                continue;
            }

            if (!node.TryGetPropertyValue("usage_report", out JsonNode? usageReportNode))
            {
                continue;
            }

            if (usageReportNode is not JsonObject usageReport)
            {
                continue;
            }

            // rate_limits null → usage-based billing; clear, not a failure.
            // Any non-object value (including JSON null) maps to NoUsageReport.
            if (!usageReport.TryGetPropertyValue("rate_limits", out JsonNode? rateLimitsNode) ||
                rateLimitsNode is not JsonObject rateLimits)
            {
                return new UsageParseResult.NoUsageReport();
            }

            return ParseRateLimits(rateLimits, accountEmail, capturedAt);
        }

        // No assistant message with usage_report found.
        return new UsageParseResult.NoUsageReport();
    }

    private static UsageParseResult ParseRateLimits(
        JsonObject rateLimits,
        string accountEmail,
        DateTimeOffset capturedAt)
    {
        if (!rateLimits.TryGetPropertyValue("limits", out JsonNode? limitsNode))
        {
            return new UsageParseResult.Unparseable();
        }

        if (limitsNode is not JsonArray limitsArray)
        {
            return new UsageParseResult.Unparseable();
        }

        List<UsageRateLimit> limits = [];

        foreach (JsonNode? item in limitsArray)
        {
            if (item is not JsonObject limitObj)
            {
                return new UsageParseResult.Unparseable();
            }

            UsageRateLimit? limit = ParseLimit(limitObj);

            if (limit is null)
            {
                return new UsageParseResult.Unparseable();
            }

            limits.Add(limit);
        }

        UsageExtraCredits? extraUsage = null;

        if (rateLimits.TryGetPropertyValue("extra_usage", out JsonNode? extraNode) &&
            extraNode is JsonObject extraObj)
        {
            extraUsage = ParseExtraUsage(extraObj);

            if (extraUsage is null)
            {
                return new UsageParseResult.Unparseable();
            }
        }

        UsageSnapshot snapshot = new(
            Limits: limits,
            ExtraUsage: extraUsage,
            CapturedAt: capturedAt,
            AccountEmail: accountEmail,
            LastRefreshFailedAt: null);

        return new UsageParseResult.Parsed(snapshot);
    }

    private static UsageRateLimit? ParseLimit(JsonObject obj)
    {
        try
        {
            string? kind = obj["kind"]?.GetValue<string>();
            string? severity = obj["severity"]?.GetValue<string>();
            string? resetsAtStr = obj["resets_at"]?.GetValue<string>();

            if (kind is null || severity is null || resetsAtStr is null)
            {
                return null;
            }

            if (!DateTimeOffset.TryParse(resetsAtStr, out DateTimeOffset resetsAt))
            {
                return null;
            }

            decimal percent = obj["percent"]?.GetValue<decimal>() ?? 0m;
            bool isActive = obj["is_active"]?.GetValue<bool>() ?? false;

            string? modelDisplayName = null;

            if (obj["scope"] is JsonObject scope &&
                scope["model"] is JsonObject model)
            {
                modelDisplayName = model["display_name"]?.GetValue<string>();
            }

            return new UsageRateLimit(
                Kind: kind,
                Percent: percent,
                ResetsAt: resetsAt,
                ModelDisplayName: modelDisplayName,
                Severity: severity,
                IsActive: isActive);
        }
        catch (InvalidOperationException)
        {
            // Token-type mismatch (e.g. string where number expected) — treat as unparseable.
            return null;
        }
    }

    private static UsageExtraCredits? ParseExtraUsage(JsonObject obj)
    {
        try
        {
            string? currency = obj["currency"]?.GetValue<string>();

            if (currency is null)
            {
                return null;
            }

            bool isEnabled = obj["is_enabled"]?.GetValue<bool>() ?? false;
            long monthlyLimit = obj["monthly_limit"]?.GetValue<long>() ?? 0L;
            long usedCredits = obj["used_credits"]?.GetValue<long>() ?? 0L;
            decimal utilization = obj["utilization"]?.GetValue<decimal>() ?? 0m;

            return new UsageExtraCredits(
                IsEnabled: isEnabled,
                MonthlyLimit: monthlyLimit,
                UsedCredits: usedCredits,
                Utilization: utilization,
                Currency: currency);
        }
        catch (InvalidOperationException)
        {
            // Token-type mismatch (e.g. string where number expected) — treat as unparseable.
            return null;
        }
    }
}
