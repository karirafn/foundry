namespace Foundry.Modules.Monitoring.Contracts;

public sealed record EligibilityViolationInfo(string Rule, string Description)
{
    public static readonly string AllowDirectPushesRule = "branch-protection:allow-direct-pushes";
    public static readonly string AllowForcePushesRule = "branch-protection:allow-force-pushes";
    public static readonly string AllowDeletionRule = "branch-protection:allow-deletion";
    public static readonly string AllowDirectPushesDescription = "Allows direct pushes to the protected branch.";
    public static readonly string AllowForcePushesDescription = "Allows force pushes to the protected branch.";
    public static readonly string AllowDeletionDescription = "Allows deletion of the protected branch.";

    public const string NoCredentialRulePrefix = "no-credential";

    public static string NoCredentialRule(string namespaceName) =>
        $"{NoCredentialRulePrefix}:{namespaceName}";

    public static string NoCredentialDescription(string namespaceName) =>
        $"No credential for namespace {namespaceName}.";

    public const string CredentialUnreadableRule = "credential-unreadable";

    public const string CredentialUnreadableDescription =
        "The account token can't be decrypted. Re-enter it on the account to resume.";

    public const string CredentialInvalidRule = "credential-invalid";

    public const string CredentialInvalidDescription =
        "The provider rejected the account token. Enter a new token on the account to resume.";

    public const string CannotPushRulePrefix = "cannot-push";

    public static string CannotPushRule(string slug) =>
        $"{CannotPushRulePrefix}:{slug}";

    public const string PackageSourceUnreachableRulePrefix = "package-source-unreachable";

    public static string PackageSourceUnreachableRule(string url) =>
        $"{PackageSourceUnreachableRulePrefix}:{url}";

    public static string PackageSourceUnreachableDescription(string url) =>
        $"Foundry can't read the package source {url}.";

    public const string NuGetConfigInvalidRule = "nuget-config-invalid";

    public const string NuGetConfigInvalidDescription =
        "The repository's root nuget.config could not be parsed.";

    /// <summary>
    /// Whether <paramref name="rule"/> is about the repository's package sources (<c>package-source-unreachable:*</c>
    /// or <c>nuget-config-invalid</c>) rather than its branch rules or provider access.
    /// </summary>
    public static bool IsPackageSourceRule(string rule) =>
        rule == NuGetConfigInvalidRule
        || rule.StartsWith(PackageSourceUnreachableRulePrefix + ":", StringComparison.Ordinal);

    /// <summary>
    /// Returns the provider-aware sentence-case description for a cannot-push violation.
    /// The provider token must be one of the ProviderTypes constants ("github" / "gitlab").
    /// </summary>
    public static string CannotPushDescription(string slug, string providerType) =>
        NoPushAccessPreamble(providerType) + $" for {slug}.";

    /// <summary>
    /// Returns the cannot-push description naming what the token lacks. <paramref name="missingPermissionLabels"/>
    /// holds display labels in permission-list order; when it is empty the generic description is returned.
    /// </summary>
    public static string CannotPushDescription(
        string slug,
        string providerType,
        IReadOnlyList<string> missingPermissionLabels)
    {
        if (missingPermissionLabels.Count == 0)
        {
            return CannotPushDescription(slug, providerType);
        }

        return providerType switch
        {
            "gitlab" => $"Token's user needs the Maintainer role on {slug}.",
            _ => $"Token is missing {JoinWithAnd(missingPermissionLabels)} for {slug}.",
        };
    }

    private static string JoinWithAnd(IReadOnlyList<string> items) =>
        items.Count == 1
            ? items[0]
            : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    /// <summary>
    /// Returns the provider-aware sentence-case no-push access explanation used in the
    /// available-repositories response and as the picker group explanation on the frontend.
    /// </summary>
    public static string NoPushAccessExplanation(string providerType) =>
        providerType switch
        {
            // The account token needs the Maintainer role on every monitored repository, so the picker names that
            // bar even though it lists Developer repositories as writable.
            "gitlab" => "Your token's role is below Maintainer.",
            _ => $"Your {NoPushAccessPreamble(providerType, lowercase: true)}.",
        };

    private static string NoPushAccessPreamble(string providerType, bool lowercase = false)
    {
        string preamble = providerType switch
        {
            "github" => "Token lacks push permission or SSO isn't authorized",
            "gitlab" => "Token's role is below Developer",
            _ => "Token cannot push",
        };

        return lowercase ? char.ToLowerInvariant(preamble[0]) + preamble[1..] : preamble;
    }
}
