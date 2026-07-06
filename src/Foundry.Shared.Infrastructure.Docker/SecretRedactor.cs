using System.Text.RegularExpressions;

namespace Foundry.Shared.Infrastructure.Docker;

/// <summary>
/// Redacts secrets from container output before persistence.
/// Rewrites HTTPS URLs with userinfo (<c>https://user@host</c> → <c>https://***@host</c>),
/// masks known token shapes (<c>glpat-</c>, <c>ghp_</c>, <c>github_pat_</c>, <c>gho_</c>, <c>sk-ant-</c>),
/// and redacts values of sensitive environment variables (<c>CLAUDE_CODE_OAUTH_TOKEN</c>, <c>ANTHROPIC_API_KEY</c>)
/// regardless of value shape.
/// </summary>
public static partial class SecretRedactor
{
    public static string Redact(string output)
    {
        string result = HttpsUserinfoPattern().Replace(output, "https://***@");
        result = SensitiveEnvVarPattern().Replace(result, "${key}=***");
        result = KnownTokenPattern().Replace(result, "***");
        return result;
    }

    // Matches https://<userinfo>@ — replaces the scheme+userinfo part
    [GeneratedRegex(
        @"https://[^@/\s]+@",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex HttpsUserinfoPattern();

    // Matches sensitive env-var assignments by key name, capturing the key and masking the value.
    // Handles opaque token values that have no known prefix shape, including single- and double-quoted values
    // that may contain internal whitespace (e.g. CLAUDE_CODE_OAUTH_TOKEN='tok en value').
    [GeneratedRegex(
        @"(?<key>CLAUDE_CODE_OAUTH_TOKEN|ANTHROPIC_API_KEY)=(?:'[^']*'|""[^""]*""|\S+)",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SensitiveEnvVarPattern();

    // Matches known PAT/API-key token shapes: glpat-, ghp_, github_pat_, gho_, sk-ant-
    // followed by the token value (non-whitespace characters).
    // The whole match (prefix + value) is replaced with *** to mask the full token.
    [GeneratedRegex(
        @"(?:glpat-|ghp_|github_pat_|gho_|sk-ant-)\S+",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex KnownTokenPattern();
}
