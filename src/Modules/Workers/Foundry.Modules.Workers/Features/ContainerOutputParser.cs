using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Features;

internal sealed partial class ContainerOutputParser : IContainerOutputParser
{
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

        string? lastJsonLine = ExtractLastJsonLine(log);

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

        if (terminalReason is null || !UsageLimitReasons.Contains(terminalReason))
        {
            return new ContainerOutputParseResult.NormalExit();
        }

        DateTimeOffset resetsAt = ParseResetTime(node["result"]?.GetValue<string>(), defaultCooldownMinutes);

        return new ContainerOutputParseResult.UsageLimited(resetsAt);
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
            Match match = ResetTimePattern().Match(resultText);

            if (match.Success && DateTimeOffset.TryParse(
                match.Value,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        return DateTimeOffset.UtcNow.AddMinutes(defaultCooldownMinutes);
    }

    [GeneratedRegex(
        @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResetTimePattern();
}
