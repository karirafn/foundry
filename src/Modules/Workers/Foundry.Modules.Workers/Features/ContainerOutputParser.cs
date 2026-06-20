using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Features;

internal sealed partial class ContainerOutputParser : IContainerOutputParser
{
    private const int MaxLogLength = 65_536;    // 64 KB
    private const int MaxJsonLineLength = 4_096; // 4 KB

    private static readonly HashSet<string> UsageLimitReasons = new(StringComparer.Ordinal)
    {
        "blocking_limit",
        "rapid_refill_breaker",
    };

    public ContainerOutputParseResult Parse(string? log, int defaultCooldownMinutes)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return new ContainerOutputParseResult.ParseFailure(log ?? string.Empty);
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
            return new ContainerOutputParseResult.ParseFailure(log);
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

        string? terminalReason = node["terminal_reason"]?.GetValue<string>();
        int? apiErrorStatus = ReadApiErrorStatus(node);

        if (!IsUsageLimit(apiErrorStatus, terminalReason))
        {
            return new ContainerOutputParseResult.NormalExit();
        }

        DateTimeOffset resetsAt = ParseResetTime(node["result"]?.GetValue<string>(), defaultCooldownMinutes);

        return new ContainerOutputParseResult.UsageLimited(resetsAt);
    }

    private static bool IsUsageLimit(int? apiErrorStatus, string? terminalReason)
    {
        return apiErrorStatus == 429
            || (terminalReason is not null && UsageLimitReasons.Contains(terminalReason));
    }

    private static int? ReadApiErrorStatus(JsonNode node)
    {
        JsonNode? statusNode = node["api_error_status"];

        if (statusNode is null)
        {
            return null;
        }

        return statusNode.GetValueKind() switch
        {
            JsonValueKind.Number => statusNode.GetValue<int>(),
            JsonValueKind.String when int.TryParse(statusNode.GetValue<string>(), out int parsed) => parsed,
            _ => null,
        };
    }

    private static string? ExtractLastJsonLine(string log)
    {
        ReadOnlySpan<char> span = log.AsSpan().TrimEnd();

        for (int i = span.Length - 1; i >= 0; i--)
        {
            if (span[i] == '\n')
            {
                ReadOnlySpan<char> candidate = span[(i + 1)..].TrimStart();

                if (!candidate.IsEmpty && candidate[0] == '{')
                {
                    return candidate.ToString();
                }
            }
        }

        // No newline found — the entire trimmed input might be the JSON line
        ReadOnlySpan<char> trimmed = span.TrimStart();

        if (!trimmed.IsEmpty && trimmed[0] == '{')
        {
            return trimmed.ToString();
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

        DateTimeOffset today = DateTimeOffset.UtcNow.Date;
        DateTimeOffset candidate = today.Add(timeOfDay.ToTimeSpan());

        if (candidate <= DateTimeOffset.UtcNow)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    [GeneratedRegex(
        @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Iso8601ResetTimePattern();

    [GeneratedRegex(
        @"(?<!\w)resets\s+(?<time>\d{1,2}:\d{2}\s*[ap]m)(?:\s*\(UTC\))?",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex WallClockResetTimePattern();
}
