using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundry.Shared.Infrastructure.Docker;

/// <summary>
/// Splits a Docker container log line into its optional timestamp prefix and body.
/// <para>
/// Logs read with <c>Timestamps = true</c> prefix every line with an RFC3339Nano date-time
/// followed by a single space (e.g. <c>2026-06-29T21:24:05.123456789Z {"type":"result"}</c>).
/// Lines without that prefix pass through unchanged.
/// </para>
/// </summary>
public static partial class DockerLogLine
{
    /// <summary>
    /// Splits <paramref name="line"/> into its Docker timestamp prefix (including the trailing
    /// space) and the remaining body. When no prefix is present, <c>Prefix</c> is empty and
    /// <c>Body</c> is the whole line.
    /// </summary>
    public static (string Prefix, string Body) Split(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Match match = TimestampPrefixPattern().Match(line);

        return match.Success
            ? (line[..match.Length], line[match.Length..])
            : (string.Empty, line);
    }

    /// <summary>
    /// Returns <paramref name="line"/> without its Docker timestamp prefix, if any.
    /// </summary>
    public static string StripTimestamp(string line)
    {
        return Split(line).Body;
    }

    /// <summary>
    /// Parses a timestamp prefix returned by <see cref="Split"/> into a
    /// <see cref="DateTimeOffset"/>; <see langword="null"/> when empty or unparseable.
    /// </summary>
    public static DateTimeOffset? ParseTimestamp(string prefix)
    {
        return !string.IsNullOrEmpty(prefix)
            && DateTimeOffset.TryParse(
                prefix.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed)
                ? parsed
                : null;
    }

    // Matches a Docker log timestamp prefix: RFC3339Nano date-time followed by a space.
    // Example: "2026-06-29T21:24:05.123456789Z " or "2026-06-29T21:24:05Z "
    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) ",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimestampPrefixPattern();
}
