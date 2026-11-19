using System.Text.RegularExpressions;

using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>Reads a proxy-probe's exit: its single <c>FOUNDRY_PROXY_PROBE</c> stdout line, or nothing conclusive.</summary>
internal static partial class PackageProxyProbeOutputParser
{
    public const string MissingResultMessage = "The Publisher proxy-probe finished without reporting a result.";

    public static PackageProxyProbeOutcome Parse(string? log)
    {
        MatchCollection lines = ResultLinePattern().Matches(log ?? string.Empty);

        if (lines.Count != 1)
        {
            return new PackageProxyProbeOutcome.Inconclusive(MissingResultMessage);
        }

        Match line = lines[0];

        return line.Groups["result"].Value == "unreachable"
            ? new PackageProxyProbeOutcome.Unreachable(SecretRedactor.Redact(line.Groups["detail"].Value))
            : new PackageProxyProbeOutcome.Reachable();
    }

    // Anchored to the start of a line (after the optional Docker timestamp), like FOUNDRY_PROBE.
    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_PROXY_PROBE result=(?<result>reachable|unreachable) detail=(?<detail>[^\r\n]*?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResultLinePattern();
}
