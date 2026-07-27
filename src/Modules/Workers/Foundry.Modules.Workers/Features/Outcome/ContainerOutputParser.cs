using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Outcome;

internal sealed partial class ContainerOutputParser : IContainerOutputParser
{
    private const int MaxLogLength = 65_536;    // 64 KB
    private const int MaxJsonLineLength = 4_096; // 4 KB

    private static readonly FrozenSet<string> UsageLimitReasons = new HashSet<string>(StringComparer.Ordinal)
    {
        "blocking_limit",
        "rapid_refill_breaker",
    }.ToFrozenSet(StringComparer.Ordinal);

    public ContainerOutputParseResult Parse(string? log, int defaultCooldownMinutes)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return new ContainerOutputParseResult.NoResultLine();
        }

        if (log.Length > MaxLogLength)
        {
            log = log[^MaxLogLength..];
        }

        string? lastJsonLine = ExtractLastJsonLine(log);

        if (lastJsonLine is not null && lastJsonLine.Length > MaxJsonLineLength)
        {
            return new ContainerOutputParseResult.ParseFailure(log);
        }

        if (lastJsonLine is null)
        {
            return BootstrapSentinelOrNoResultLine(log);
        }

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(lastJsonLine);
        }
        catch (JsonException)
        {
            return new ContainerOutputParseResult.ParseFailure(log);
        }

        if (node is null)
        {
            return new ContainerOutputParseResult.ParseFailure(log);
        }

        string? terminalReason = ReadString(node["terminal_reason"]);
        int? apiErrorStatus = ReadApiErrorStatus(node);

        if (IsUsageLimit(apiErrorStatus, terminalReason))
        {
            DateTimeOffset resetsAt = ParseResetTime(ReadString(node["result"]), defaultCooldownMinutes);

            return new ContainerOutputParseResult.UsageLimited(resetsAt);
        }

        if (IsAuthInvalid(apiErrorStatus, node))
        {
            return new ContainerOutputParseResult.AuthInvalid();
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

        string? lastJsonLine = ExtractLastJsonLine(log);

        if (lastJsonLine is null || lastJsonLine.Length > MaxJsonLineLength)
        {
            return null;
        }

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(lastJsonLine);
        }
        catch (JsonException)
        {
            return null;
        }

        if (node is null)
        {
            return null;
        }

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

    private static string? ExtractLastJsonLine(string log)
    {
        ReadOnlySpan<char> span = log.AsSpan().TrimEnd();

        for (int i = span.Length - 1; i >= 0; i--)
        {
            if (span[i] == '\n')
            {
                ReadOnlySpan<char> candidate = span[(i + 1)..].TrimStart();
                string? jsonLine = StripDockerTimestampAndExtractJson(candidate);

                if (jsonLine is not null)
                {
                    return jsonLine;
                }
            }
        }

        // No newline found — the entire trimmed input might be the JSON line
        ReadOnlySpan<char> trimmed = span.TrimStart();
        return StripDockerTimestampAndExtractJson(trimmed);
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
        string candidateStr = candidate.ToString();
        System.Text.RegularExpressions.Match match = DockerTimestampPrefixPattern().Match(candidateStr);

        if (!match.Success)
        {
            return null;
        }

        ReadOnlySpan<char> afterTimestamp = candidateStr.AsSpan(match.Length).TrimStart();

        if (!afterTimestamp.IsEmpty && afterTimestamp[0] == '{')
        {
            return afterTimestamp.ToString();
        }

        return null;
    }

    private static DateTimeOffset ParseResetTime(string? resultText, int defaultCooldownMinutes)
    {
        if (resultText is not null)
        {
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
                DateTimeOffset? wallClockTime = ParseWallClockTime(wallClockMatch.Groups["time"].Value);

                if (wallClockTime is not null)
                {
                    return wallClockTime.Value;
                }
            }
        }

        return DateTimeOffset.UtcNow.AddMinutes(defaultCooldownMinutes);
    }

    private static DateTimeOffset? ParseWallClockTime(string timeText)
    {
        if (!TimeOnly.TryParse(timeText, CultureInfo.InvariantCulture, out TimeOnly timeOfDay))
        {
            return null;
        }

        DateTimeOffset today = new(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        DateTimeOffset candidate = today.Add(timeOfDay.ToTimeSpan());

        if (candidate <= DateTimeOffset.UtcNow)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
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

    [GeneratedRegex(
        @"(?<!\w)resets\s+(?<time>\d{1,2}:\d{2}\s*[ap]m)\s*\(UTC\)",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex WallClockResetTimePattern();

    [GeneratedRegex(
        @"FOUNDRY_BOOTSTRAP_FAILED stage=(?:clone|auth|branch)[^\n]*",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex BootstrapSentinelPattern();

    // Matches a Docker log timestamp prefix: RFC3339Nano date-time followed by a space.
    // Example: "2026-06-29T21:24:05.123456789Z " or "2026-06-29T21:24:05Z "
    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) ",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex DockerTimestampPrefixPattern();
}
