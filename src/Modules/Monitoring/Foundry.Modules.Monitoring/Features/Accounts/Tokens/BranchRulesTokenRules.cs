using System.Text.RegularExpressions;

using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

/// <summary>
/// Shape rules for the optional branch-rules token (ADR 0084). The token's permissions are
/// deliberately not validated on save — they are established by attempting the write.
/// </summary>
internal static partial class BranchRulesTokenRules
{
    internal const string EmptyCode = "BranchRulesToken.Empty";
    internal const string TooLongCode = "BranchRulesToken.TooLong";
    internal const string InvalidCharsCode = "BranchRulesToken.InvalidChars";

    [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$")]
    private static partial Regex ValidTokenCharactersRegex();

    /// <summary>
    /// Validates a supplied branch-rules token. <see langword="null"/> means "not supplied" and is valid.
    /// </summary>
    internal static Result Validate(string? token)
    {
        if (token is null)
        {
            return Result.Ok();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new Error(EmptyCode, "Branch-rules token must not be empty when supplied.");
        }

        if (token.Length > AccountsDatabaseHelpers.TokenMaxLength)
        {
            return new Error(
                TooLongCode,
                $"Branch-rules token must not exceed {AccountsDatabaseHelpers.TokenMaxLength} characters.");
        }

        if (!ValidTokenCharactersRegex().IsMatch(token))
        {
            return new Error(
                InvalidCharsCode,
                "Branch-rules token contains invalid characters. Only alphanumeric characters, hyphens, underscores, and dots are allowed.");
        }

        return Result.Ok();
    }
}
