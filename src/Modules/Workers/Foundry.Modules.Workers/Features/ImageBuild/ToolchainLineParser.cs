using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>Reads the single <c>FOUNDRY_TOOLCHAIN</c> line <c>workers/toolchain.sh</c> prints (ADR 0109).</summary>
internal static partial class ToolchainLineParser
{
    /// <summary>
    /// The toolchain, or <see langword="null"/> unless the log holds exactly one <c>FOUNDRY_TOOLCHAIN</c> line and its
    /// list is valid.
    /// </summary>
    public static Toolchain? Parse(string? log)
    {
        MatchCollection lines = ToolchainLinePattern().Matches(log ?? string.Empty);

        return lines.Count == 1 ? ToolchainJson.TryDeserialize(lines[0].Groups["json"].Value) : null;
    }

    // Anchored to the start of a line (after the optional Docker timestamp), like FOUNDRY_PROBE.
    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_TOOLCHAIN (?<json>[^\r\n]*?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ToolchainLinePattern();
}
