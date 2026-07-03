using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Features.Login;

internal static partial class AuthorizationUrlExtractor
{
    // Matches a URL that contains "oauth" with no whitespace — the Claude CLI line looks like:
    // "If the browser didn't open, visit: https://claude.com/cai/oauth/authorize?..."
    [GeneratedRegex(@"https?://\S*oauth\S*")]
    private static partial Regex OAuthUrlRegex();

    /// <summary>
    /// Extracts the OAuth authorize URL from a single stdout log line.
    /// Returns <c>null</c> when the line contains no matching URL.
    /// </summary>
    internal static string? Extract(string line)
    {
        Match match = OAuthUrlRegex().Match(line);
        return match.Success ? match.Value : null;
    }
}
