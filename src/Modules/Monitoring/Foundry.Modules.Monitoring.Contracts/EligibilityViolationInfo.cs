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

    public const string CannotPushRulePrefix = "cannot-push";

    public static string CannotPushRule(string slug) =>
        $"{CannotPushRulePrefix}:{slug}";

    /// <summary>
    /// Returns the provider-aware sentence-case description for a cannot-push violation.
    /// The provider token must be one of the ProviderTypes constants ("github" / "gitlab").
    /// </summary>
    public static string CannotPushDescription(string slug, string providerType) =>
        NoPushAccessPreamble(providerType) + $" for {slug}.";

    /// <summary>
    /// Returns the provider-aware sentence-case no-push access explanation used in the
    /// available-repositories response and as the picker group explanation on the frontend.
    /// </summary>
    public static string NoPushAccessExplanation(string providerType) =>
        $"Your {NoPushAccessPreamble(providerType, lowercase: true)}.";

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
