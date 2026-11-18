using System.Collections.Frozen;

using Foundry.Modules.Monitoring.Infrastructure.GitHub;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

/// <summary>How Foundry checks that an account token holds a <see cref="TokenPermission"/>.</summary>
internal abstract record PermissionVerification
{
    private PermissionVerification() { }

    /// <summary>Verified by the GitHub write probe for <paramref name="Permission"/>.</summary>
    internal sealed record WriteProbe(WritePermission Permission) : PermissionVerification;

    /// <summary>Verified by the token user's GitLab role on the repository reaching <paramref name="MinimumAccessLevel"/>.</summary>
    internal sealed record GitLabRole(int MinimumAccessLevel) : PermissionVerification;

    /// <summary>Verified by the token's scopes when the token is validated.</summary>
    internal sealed record Scope : PermissionVerification;

    /// <summary>Not verified before use.</summary>
    internal sealed record NotVerified : PermissionVerification;
}

/// <summary>A GitHub fine-grained token creation-URL parameter, such as <c>contents=write</c>.</summary>
internal sealed record TemplateParameter(string Name, string Level)
{
    public override string ToString() => $"{Name}={Level}";
}

/// <param name="Label">The text the account form shows for the permission.</param>
/// <param name="TemplateParameter">The GitHub creation-URL parameter that preselects it, when it has one.</param>
/// <param name="ClassicScope">The GitHub classic OAuth scope that grants it, when it has one.</param>
/// <param name="VerifiedBy">How Foundry checks the token holds it.</param>
/// <param name="ShownInForm">Whether the account form lists it.</param>
internal sealed record TokenPermission(
    string Label,
    TemplateParameter? TemplateParameter,
    string? ClassicScope,
    PermissionVerification VerifiedBy,
    bool ShownInForm = true);

/// <summary>
/// The one list per provider of what an account token needs. The account form, the GitHub creation
/// link, the classic-scope check, the write probes and the GitLab minimum role all derive from it, so
/// a permission is added in one place (ADR 0115).
/// </summary>
internal static class TokenPermissions
{
    private const string WriteLevel = "write";
    private const string RepoScope = "repo";
    private const int GitLabDeveloperAccessLevel = 30;

    private static readonly IReadOnlyList<TokenPermission> GitHub =
    [
        new(
            "Contents (read and write)",
            new TemplateParameter("contents", WriteLevel),
            RepoScope,
            new PermissionVerification.WriteProbe(WritePermission.Contents)),
        new(
            "Issues (read and write)",
            new TemplateParameter("issues", WriteLevel),
            RepoScope,
            new PermissionVerification.WriteProbe(WritePermission.Issues)),
        new(
            "Pull requests (read and write)",
            new TemplateParameter("pull_requests", WriteLevel),
            RepoScope,
            new PermissionVerification.WriteProbe(WritePermission.PullRequests)),
        new(
            "Workflows (write)",
            new TemplateParameter("workflows", WriteLevel),
            ClassicScope: null,
            new PermissionVerification.NotVerified()),
        new("Metadata (read)", TemplateParameter: null, ClassicScope: null, new PermissionVerification.NotVerified()),
        new("Commit statuses (read)", TemplateParameter: null, ClassicScope: null, new PermissionVerification.NotVerified()),
        new("Actions (read)", TemplateParameter: null, ClassicScope: null, new PermissionVerification.NotVerified()),
    ];

    // The role entry stays out of the form until #1100 shows it as the Maintainer role.
    private static readonly IReadOnlyList<TokenPermission> GitLab =
    [
        new("api", TemplateParameter: null, ClassicScope: null, new PermissionVerification.Scope()),
        new(
            "Developer role",
            TemplateParameter: null,
            ClassicScope: null,
            new PermissionVerification.GitLabRole(GitLabDeveloperAccessLevel),
            ShownInForm: false),
    ];

    private static readonly FrozenDictionary<string, IReadOnlyList<TokenPermission>> ByProvider =
        new Dictionary<string, IReadOnlyList<TokenPermission>>
        {
            [ProviderTypes.GitHub] = GitHub,
            [ProviderTypes.GitLab] = GitLab,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<TokenPermission> For(string providerType) =>
        ByProvider.TryGetValue(providerType, out IReadOnlyList<TokenPermission>? entries) ? entries : [];

    /// <summary>The GitHub write probes, in the order they run.</summary>
    internal static IReadOnlyList<WritePermission> GitHubWriteProbes { get; } = GitHub
        .Select(entry => entry.VerifiedBy)
        .OfType<PermissionVerification.WriteProbe>()
        .Select(probe => probe.Permission)
        .ToList();

    /// <summary>The OAuth scopes a classic GitHub token must carry.</summary>
    internal static IReadOnlyList<string> GitHubClassicScopes { get; } = GitHub
        .Select(entry => entry.ClassicScope)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>The creation-URL query that preselects every GitHub permission with a template parameter.</summary>
    internal static string GitHubTemplateQuery { get; } = string.Join(
        '&',
        GitHub
            .Select(entry => entry.TemplateParameter)
            .OfType<TemplateParameter>()
            .Select(parameter => parameter.ToString()));

    /// <summary>The GitLab access level the token's user needs on a repository.</summary>
    internal static int GitLabMinimumAccessLevel { get; } = GitLab
        .Select(entry => entry.VerifiedBy)
        .OfType<PermissionVerification.GitLabRole>()
        .Single()
        .MinimumAccessLevel;

    internal static string LabelOf(WritePermission permission) =>
        GitHub.Single(entry => entry.VerifiedBy is PermissionVerification.WriteProbe probe && probe.Permission == permission)
            .Label;
}
