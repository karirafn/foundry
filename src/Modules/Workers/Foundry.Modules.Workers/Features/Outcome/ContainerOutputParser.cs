using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Outcome;

internal sealed partial class ContainerOutputParser(ILogger<ContainerOutputParser> logger) : IContainerOutputParser
{
    private const int MaxLogLength = 65_536;    // 64 KB
    // stream-json's result event carries today's json keys plus model_usage_since_last_result,
    // so a real result line with a multi-paragraph summary routinely exceeds the former 4 KB cap.
    private const int MaxJsonLineLength = 16_384; // 16 KB

    private static readonly FrozenSet<string> UsageLimitReasons = new HashSet<string>(StringComparer.Ordinal)
    {
        "blocking_limit",
        "rapid_refill_breaker",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TransientApiErrorPhrases = new HashSet<string>(StringComparer.Ordinal)
    {
        "API Error: Connection closed mid-response",
        "API Error: 529 Overloaded",
    }.ToFrozenSet(StringComparer.Ordinal);

    public ContainerOutputParseResult Parse(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return new ContainerOutputParseResult.NoResultLine();
        }

        if (log.Length > MaxLogLength)
        {
            log = log[^MaxLogLength..];
        }

        (string Line, JsonObject Node)? resultLine = ExtractLastResultLine(log);

        if (resultLine is null)
        {
            return BootstrapSentinelOrNoResultLine(log);
        }

        if (resultLine.Value.Line.Length > MaxJsonLineLength)
        {
            return new ContainerOutputParseResult.ParseFailure(log);
        }

        JsonObject node = resultLine.Value.Node;

        string? terminalReason = ReadString(node["terminal_reason"]);
        int? apiErrorStatus = ReadApiErrorStatus(node);
        bool isError = ReadBool(node["is_error"]);
        string? resultText = ReadString(node["result"]);

        if (IsUsageLimit(apiErrorStatus, terminalReason))
        {
            DateTimeOffset? resetsAt = TryParseResetTime(resultText);

            return resetsAt is not null
                ? new ContainerOutputParseResult.UsageLimited(resetsAt.Value)
                : new ContainerOutputParseResult.CreditsExhausted();
        }

        if (IsAuthInvalid(apiErrorStatus, node))
        {
            return new ContainerOutputParseResult.AuthInvalid();
        }

        if (IsTransientApiError(apiErrorStatus, isError, resultText))
        {
            return new ContainerOutputParseResult.TransientApiError();
        }

        if (isError)
        {
            logger.LogWarning(
                "Unclassified worker error — result text: {ResultText}",
                SanitizeForLog(resultText));
        }

        return new ContainerOutputParseResult.NormalExit();
    }

    public RunResultSummary? ParseRunResultSummary(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return null;
        }

        if (log.Length > MaxLogLength)
        {
            log = log[^MaxLogLength..];
        }

        (string Line, JsonObject Node)? resultLine = ExtractLastResultLine(log);

        if (resultLine is null || resultLine.Value.Line.Length > MaxJsonLineLength)
        {
            return null;
        }

        JsonObject node = resultLine.Value.Node;

        string? resultText = ReadString(node["result"]);
        string? subtype = ReadString(node["subtype"]);
        bool isError = ReadBool(node["is_error"]);
        long durationMs = ReadLong(node["duration_ms"]);
        int numTurns = ReadInt(node["num_turns"]);
        decimal? totalCostUsd = ReadDecimal(node["total_cost_usd"]);
        int? inputTokens = ReadNullableInt(node["usage"]?["input_tokens"]);
        int? outputTokens = ReadNullableInt(node["usage"]?["output_tokens"]);

        return RunResultSummary.Create(
            resultText,
            subtype,
            isError,
            durationMs,
            numTurns,
            totalCostUsd,
            inputTokens,
            outputTokens);
    }

    private const int MaxLoggedResultTextLength = 200;

    // Sanitize untrusted worker output before logging to prevent log injection.
    // Strips CR and LF characters (which could forge a second log line) and caps length.
    private static string? SanitizeForLog(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string sanitized = text
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        if (sanitized.Length > MaxLoggedResultTextLength)
        {
            sanitized = sanitized[..MaxLoggedResultTextLength];
        }

        return sanitized;
    }

    private static bool ReadBool(JsonNode? node)
    {
        return node is not null && node.GetValueKind() == JsonValueKind.True;
    }

    private static long ReadLong(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.Number)
        {
            return 0;
        }

        return node.GetValue<long>();
    }

    private static int ReadInt(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.Number)
        {
            return 0;
        }

        long value = node.GetValue<long>();

        if (value < int.MinValue || value > int.MaxValue)
        {
            return 0;
        }

        return (int)value;
    }

    private static int? ReadNullableInt(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        long value = node.GetValue<long>();

        if (value < int.MinValue || value > int.MaxValue)
        {
            return null;
        }

        return (int)value;
    }

    private static decimal? ReadDecimal(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        return node.GetValue<decimal>();
    }

    private static bool IsUsageLimit(int? apiErrorStatus, string? terminalReason)
    {
        return apiErrorStatus == 429
            || (terminalReason is not null && UsageLimitReasons.Contains(terminalReason));
    }

    private static bool IsAuthInvalid(int? apiErrorStatus, JsonNode node)
    {
        if (apiErrorStatus == 401)
        {
            return true;
        }

        // Secondary guard: error.type == "authentication_error" (shape assumed — no real fixture found)
        string? errorType = ReadString(node["error"]?["type"]);

        return errorType is not null
            && string.Equals(errorType, "authentication_error", StringComparison.Ordinal);
    }

    private static bool IsTransientApiError(int? apiErrorStatus, bool isError, string? resultText)
    {
        if (apiErrorStatus is >= 500 and <= 599)
        {
            return true;
        }

        if (isError && resultText is not null)
        {
            foreach (string phrase in TransientApiErrorPhrases)
            {
                if (resultText.Contains(phrase, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? ReadString(JsonNode? node)
    {
        return node is not null && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;
    }

    private static int? ReadApiErrorStatus(JsonNode node)
    {
        JsonNode? statusNode = node["api_error_status"];

        if (statusNode is null)
        {
            return null;
        }

        if (statusNode.GetValueKind() == JsonValueKind.Number)
        {
            long asLong = statusNode.GetValue<long>();

            if (asLong < int.MinValue || asLong > int.MaxValue)
            {
                return null;
            }

            return (int)asLong;
        }

        if (statusNode.GetValueKind() == JsonValueKind.String
            && int.TryParse(statusNode.GetValue<string>(), out int parsed))
        {
            return parsed;
        }

        return null;
    }

    // Walks the log backward and returns the last line that parses as a JSON object whose
    // "type" is "result". stream-json emits one event per line; every other event type, unknown
    // types, and partial lines (a container killed mid-event) are skipped. One run can emit
    // several result events (a transient-retry resume, or a background subagent re-waking the
    // session), and the last one wins.
    private static (string Line, JsonObject Node)? ExtractLastResultLine(string log)
    {
        ReadOnlySpan<char> span = log.AsSpan().TrimEnd();
        int end = span.Length;

        for (int i = span.Length - 1; i >= -1; i--)
        {
            if (i >= 0 && span[i] != '\n')
            {
                continue;
            }

            ReadOnlySpan<char> candidate = span[(i + 1)..end].Trim();
            end = i;

            string? jsonLine = StripDockerTimestampAndExtractJson(candidate);

            if (jsonLine is not null && TryParseResultEvent(jsonLine) is { } node)
            {
                return (jsonLine, node);
            }
        }

        return null;
    }

    private static JsonObject? TryParseResultEvent(string jsonLine)
    {
        // Cheap pre-filter: most stream-json lines are not result events, so skip the full parse.
        if (!jsonLine.Contains("\"result\"", StringComparison.Ordinal))
        {
            return null;
        }

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(jsonLine);
        }
        catch (JsonException)
        {
            return null;
        }

        return node is JsonObject obj
            && string.Equals(ReadString(obj["type"]), "result", StringComparison.Ordinal)
                ? obj
                : null;
    }

    private static string? StripDockerTimestampAndExtractJson(ReadOnlySpan<char> candidate)
    {
        if (candidate.IsEmpty)
        {
            return null;
        }

        if (candidate[0] == '{')
        {
            return candidate.ToString();
        }

        // Strip optional Docker RFC3339Nano timestamp prefix (e.g. "2026-06-29T21:24:05.123456789Z ")
        (string prefix, string body) = DockerLogLine.Split(candidate.ToString());

        if (prefix.Length == 0)
        {
            return null;
        }

        ReadOnlySpan<char> afterTimestamp = body.AsSpan().TrimStart();

        if (!afterTimestamp.IsEmpty && afterTimestamp[0] == '{')
        {
            return afterTimestamp.ToString();
        }

        return null;
    }

    private static DateTimeOffset? TryParseResetTime(string? resultText)
    {
        if (resultText is null)
        {
            return null;
        }

        Match isoMatch = Iso8601ResetTimePattern().Match(resultText);

        if (isoMatch.Success && DateTimeOffset.TryParse(
            isoMatch.Value,
            null,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed))
        {
            return parsed;
        }

        Match wallClockMatch = WallClockResetTimePattern().Match(resultText);

        if (wallClockMatch.Success)
        {
            string timeText = wallClockMatch.Groups["time"].Value;
            string minutesText = wallClockMatch.Groups["minutes"].Value;
            string timezoneText = wallClockMatch.Groups["timezone"].Value;

            // When minutes are absent (bare hour like "3pm"), insert ":00" before the am/pm suffix
            // so TimeOnly.TryParse receives a valid "3:00pm" form.
            string normalizedTime = string.IsNullOrEmpty(minutesText)
                ? NormalizedBareHourTime(timeText)
                : timeText;

            return ParseWallClockTime(normalizedTime, timezoneText);
        }

        return null;
    }

    // Converts a bare-hour am/pm form ("3pm", "3 pm", "12am") to a form TimeOnly.TryParse accepts ("3:00pm", "12:00am").
    // Inserts ":00" immediately before the am/pm suffix, trimming any whitespace between the digit and the suffix.
    private static string NormalizedBareHourTime(string timeText)
    {
        // Find where the am/pm starts (after the digit part, possibly with whitespace)
        int suffixIndex = timeText.IndexOfAny(['a', 'p', 'A', 'P']);

        if (suffixIndex < 0)
        {
            return timeText;
        }

        // Trim trailing whitespace off the digit portion so "3 pm" produces "3:00pm", not "3 :00pm"
        return timeText[..suffixIndex].TrimEnd() + ":00" + timeText[suffixIndex..];
    }

    private static DateTimeOffset? ParseWallClockTime(string timeText, string timezoneText)
    {
        if (!TimeOnly.TryParse(timeText, CultureInfo.InvariantCulture, out TimeOnly timeOfDay))
        {
            return null;
        }

        if (string.Equals(timezoneText, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            DateTimeOffset today = new(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
            DateTimeOffset candidate = today.Add(timeOfDay.ToTimeSpan());

            if (candidate <= DateTimeOffset.UtcNow)
            {
                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        // IANA timezone — look up via timezone database
        // Cap length before BCL lookup: real IANA IDs are well under 64 chars; anything longer is garbage input
        if (timezoneText.Length > 64)
        {
            return null;
        }

        TimeZoneInfo zone;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneText);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            // Unknown or malformed timezone string — defence-in-depth for FindSystemTimeZoneById.
            // DST spring-forward gaps for ConvertTimeToUtc are handled separately by the IsInvalidTime guards below.
            return null;
        }

        DateTimeOffset nowInZone = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        DateTime todayInZone = nowInZone.Date;
        DateTime candidateInZone = todayInZone.Add(timeOfDay.ToTimeSpan());

        // Guard against DST spring-forward gap: a time falling in the gap is invalid for ConvertTimeToUtc
        // and would throw ArgumentException. Advance past the gap (by 1 hour) so the conversion always
        // receives a valid local time — a real reset at 2:30am during the spring-forward resolves to 3:30am.
        if (zone.IsInvalidTime(candidateInZone))
        {
            candidateInZone = candidateInZone.AddHours(1);
        }

        DateTimeOffset candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidateInZone, zone);

        if (candidateUtc <= DateTimeOffset.UtcNow)
        {
            candidateInZone = candidateInZone.AddDays(1);

            // Apply the same DST guard on the rolled-forward candidate
            if (zone.IsInvalidTime(candidateInZone))
            {
                candidateInZone = candidateInZone.AddHours(1);
            }

            candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidateInZone, zone);
        }

        return candidateUtc;
    }

    private const int MaxBootstrapDetailLength = 500;

    private static ContainerOutputParseResult BootstrapSentinelOrNoResultLine(string log)
    {
        Match match = BootstrapSentinelPattern().Match(log);

        if (!match.Success)
        {
            return new ContainerOutputParseResult.NoResultLine();
        }

        string detail = match.Value.Trim();

        if (detail.Length > MaxBootstrapDetailLength)
        {
            detail = detail[..MaxBootstrapDetailLength];
        }

        return new ContainerOutputParseResult.WorkerBootstrapFailed(detail);
    }

    [GeneratedRegex(
        @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Iso8601ResetTimePattern();

    // Matches both "resets <time> (UTC|IANA)" and "reset at <time> (UTC|IANA)".
    // Minutes are optional — a bare hour like "3pm" is captured without ":MM" in the <minutes> group,
    // and the caller defaults minutes to 00 when the group is empty.
    // The timezone group is REQUIRED — a bare "resets 11:59pm" with no zone falls through.
    [GeneratedRegex(
        @"(?<!\w)(?:resets|reset\s+at)\s+(?<time>\d{1,2}(?<minutes>:\d{2})?\s*[ap]m)\s*\((?<timezone>[^)]+)\)",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex WallClockResetTimePattern();

    [GeneratedRegex(
        @"FOUNDRY_BOOTSTRAP_FAILED stage=(?:clone|auth|branch|host-settings)[^\n]*",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex BootstrapSentinelPattern();
}
