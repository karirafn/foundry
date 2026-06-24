using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Features;

/// <summary>
/// Redacts secrets from container output before persistence.
/// Rewrites HTTPS URLs with userinfo (<c>https://user@host</c> → <c>https://***@host</c>)
/// and masks known token shapes (<c>glpat-</c>, <c>ghp_</c>, <c>github_pat_</c>, <c>gho_</c>, <c>sk-ant-</c>).
/// </summary>
internal static partial class SecretRedactor
{
    internal static string Redact(string output)
    {
        string result = HttpsUserinfoPattern().Replace(output, "https://***@");
        result = KnownTokenPattern().Replace(result, "***");
        return result;
    }

    // Matches https://<userinfo>@ — replaces the scheme+userinfo part
    [GeneratedRegex(
        @"https://[^@/\s]+@",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex HttpsUserinfoPattern();

    // Matches known PAT/API-key token shapes: glpat-, ghp_, github_pat_, gho_, sk-ant-
    // followed by the token value (non-whitespace characters).
    // The whole match (prefix + value) is replaced with *** to mask the full token.
    [GeneratedRegex(
        @"(?:glpat-|ghp_|github_pat_|gho_|sk-ant-)\S+",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex KnownTokenPattern();
}
