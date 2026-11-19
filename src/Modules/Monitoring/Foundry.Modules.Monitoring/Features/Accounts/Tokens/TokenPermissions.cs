using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

/// <summary>How Foundry checks that an account token holds a <see cref="TokenPermission"/>.</summary>
internal abstract record PermissionVerification
{
    private PermissionVerification() { }

    /// <summary>
    /// What the permission-list version hashes for this kind of check, or <see langword="null"/> when
    /// Foundry does not check the permission ahead of use.
    /// </summary>
    internal abstract string? VersionToken { get; }

    /// <summary>The wire name the token requirements list renders this kind of check by.</summary>
    internal abstract string WireName { get; }

    /// <summary>Verified by the GitHub write probe for <paramref name="Permission"/>.</summary>
    internal sealed record WriteProbe(WritePermission Permission) : PermissionVerification
    {
        internal override string VersionToken => $"write-probe:{Permission}";
        internal override string WireName => "probe";
    }

    /// <summary>Verified by the GitHub read probe for <paramref name="Permission"/>.</summary>
    internal sealed record ReadProbe(WritePermission Permission) : PermissionVerification
    {
        internal override string VersionToken => $"read-probe:{Permission}";
        internal override string WireName => "probe";
    }

    /// <summary>Verified by the token user's GitLab role on the repository reaching <paramref name="MinimumAccessLevel"/>.</summary>
    internal sealed record GitLabRole(int MinimumAccessLevel) : PermissionVerification
    {
        internal override string VersionToken => $"gitlab-role:{MinimumAccessLevel}";
        internal override string WireName => "role";
    }

    /// <summary>Verified by the token's scopes when the token is validated.</summary>
    internal sealed record Scope : PermissionVerification
    {
        internal override string VersionToken => "scope";
        internal override string WireName => "scope";
    }

    /// <summary>Every token holds it.</summary>
    internal sealed record AlwaysGranted : PermissionVerification
    {
        internal override string? VersionToken => null;
        internal override string WireName => "always-granted";
    }

    /// <summary>Cannot be verified before use.</summary>
    internal sealed record NotVerified : PermissionVerification
    {
        internal override string? VersionToken => null;
        internal override string WireName => "unverifiable";
    }
}

/// <summary>A GitHub fine-grained token creation-URL parameter, such as <c>contents=write</c>.</summary>
internal sealed record TemplateParameter(string Name, string Level)
{
    public override string ToString() => $"{Name}={Level}";
}

/// <param name="Key">The stable identifier a <c>Denied</c> verdict stores; never shown.</param>
/// <param name="Label">The text the account form shows for the permission.</param>
/// <param name="TemplateParameter">The GitHub creation-URL parameter that preselects it, when it has one.</param>
/// <param name="ClassicScope">The GitHub classic OAuth scope that grants it, when it has one.</param>
/// <param name="VerifiedBy">How Foundry checks the token holds it.</param>
internal sealed record TokenPermission(
    string Key,
    string Label,
    TemplateParameter? TemplateParameter,
    string? ClassicScope,
    PermissionVerification VerifiedBy);

/// <summary>
/// The one list per provider of what an account token needs. The account form, the GitHub creation
/// link, the classic-scope check, the probes, the GitLab minimum role and the permission-list version
/// all derive from it, so a permission is added in one place (ADR 0115).
/// </summary>
internal static class TokenPermissions
{
    private const string WriteLevel = "write";
    private const string ReadLevel = "read";
    private const string RepoScope = "repo";
    private const int GitLabMaintainerAccessLevel = 40;

    private static readonly IReadOnlyList<TokenPermission> GitHub =
    [
        new(
            "contents",
            "Contents (read and write)",
            new TemplateParameter("contents", WriteLevel),
            RepoScope,
            new PermissionVerification.WriteProbe(WritePermission.Contents)),
        new(
            "issues",
            "Issues (read and write)",
            new TemplateParameter("issues", WriteLevel),
            RepoScope,
            new PermissionVerification.WriteProbe(WritePermission.Issues)),
        new(
            "pull_requests",
            "Pull requests (read and write)",
            new TemplateParameter("pull_requests", WriteLevel),
            RepoScope,
            new PermissionVerification.WriteProbe(WritePermission.PullRequests)),
        new(
            "administration",
            "Administration (read and write)",
            new TemplateParameter("administration", WriteLevel),
            RepoScope,
            new PermissionVerification.WriteProbe(WritePermission.Administration)),
        new(
            "repository_hooks",
            "Webhooks (read and write)",
            new TemplateParameter("repository_hooks", WriteLevel),
            RepoScope,
            new PermissionVerification.WriteProbe(WritePermission.Webhooks)),
        new(
            "statuses",
            "Commit statuses (read)",
            new TemplateParameter("statuses", ReadLevel),
            RepoScope,
            new PermissionVerification.ReadProbe(WritePermission.CommitStatuses)),
        new(
            "actions",
            "Actions (read)",
            new TemplateParameter("actions", ReadLevel),
            RepoScope,
            new PermissionVerification.ReadProbe(WritePermission.Actions)),
        new(
            "metadata",
            "Metadata (read)",
            TemplateParameter: null,
            ClassicScope: null,
            new PermissionVerification.AlwaysGranted()),
        new(
            "workflows",
            "Workflows (read and write)",
            new TemplateParameter("workflows", WriteLevel),
            ClassicScope: null,
            new PermissionVerification.NotVerified()),
    ];

    private static readonly IReadOnlyList<TokenPermission> GitLab =
    [
        new("api", "api", TemplateParameter: null, ClassicScope: null, new PermissionVerification.Scope()),
        new(
            "maintainer_role",
            "Maintainer role",
            TemplateParameter: null,
            ClassicScope: null,
            new PermissionVerification.GitLabRole(GitLabMaintainerAccessLevel)),
    ];

    /// <summary>
    /// What a classic GitHub token lacks when the Administration or Webhooks probe is refused: its
    /// <c>repo</c> scope already covers both, so only the user's admin role on the repository can grant them.
    /// </summary>
    internal static TokenPermission GitHubClassicAdminRole { get; } = new(
        "admin_role",
        "Admin role on the repository",
        TemplateParameter: null,
        ClassicScope: null,
        new PermissionVerification.NotVerified());

    private static readonly FrozenSet<WritePermission> GrantedByClassicAdminRole =
        [WritePermission.Administration, WritePermission.Webhooks];

    private static readonly FrozenDictionary<string, IReadOnlyList<TokenPermission>> ByProvider =
        new Dictionary<string, IReadOnlyList<TokenPermission>>
        {
            [ProviderTypes.GitHub] = GitHub,
            [ProviderTypes.GitLab] = GitLab,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<TokenPermission> For(string providerType) =>
        ByProvider.TryGetValue(providerType, out IReadOnlyList<TokenPermission>? entries) ? entries : [];

    /// <summary>What the account form and setup wizard list for <paramref name="providerType"/>, in list order.</summary>
    internal static IReadOnlyList<TokenPermissionRequirement> RequirementsFor(string providerType) =>
        For(providerType)
            .Select(entry => new TokenPermissionRequirement(entry.Label, entry.VerifiedBy.WireName))
            .ToList();

    /// <summary>The GitHub probes, write and read, in the order they run.</summary>
    internal static IReadOnlyList<WritePermission> GitHubProbes { get; } = GitHub
        .Select(ProbedPermissionOf)
        .OfType<WritePermission>()
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

    /// <summary>The scopes a GitLab token must carry, checked when the token is validated.</summary>
    internal static IReadOnlyList<string> GitLabScopes { get; } = GitLab
        .Where(entry => entry.VerifiedBy is PermissionVerification.Scope)
        .Select(entry => entry.Label)
        .ToList();

    /// <summary>The GitLab access level the token's user needs on a repository.</summary>
    internal static int GitLabMinimumAccessLevel { get; } = GitLab
        .Select(entry => entry.VerifiedBy)
        .OfType<PermissionVerification.GitLabRole>()
        .Single()
        .MinimumAccessLevel;

    /// <summary>The key a GitLab <c>Denied</c> verdict stores for a role below the minimum.</summary>
    internal static string GitLabRoleKey { get; } = GitLab
        .Single(entry => entry.VerifiedBy is PermissionVerification.GitLabRole)
        .Key;

    private static readonly FrozenDictionary<string, string> Versions =
        ByProvider.ToFrozenDictionary(pair => pair.Key, pair => ComputeVersion(pair.Key, pair.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>The permission-list version of every provider; a verdict carrying any other version is outdated.</summary>
    internal static IReadOnlySet<string> CurrentVersions { get; } = Versions.Values.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The version of <paramref name="providerType"/>'s list: a hash of its verified entries, so adding or
    /// changing a verified entry changes it and every verdict checked against the old list is probed again.
    /// </summary>
    internal static string VersionFor(string providerType) =>
        Versions.TryGetValue(providerType, out string? version) ? version : ComputeVersion(providerType, []);

    /// <summary>
    /// The keys of the GitHub permissions a token lacks, in list order. For a classic token a refused
    /// Administration or Webhooks probe becomes the single <see cref="GitHubClassicAdminRole"/>.
    /// </summary>
    internal static IReadOnlyList<string> GitHubMissingKeys(IReadOnlyCollection<WritePermission> missing, bool isFineGrainedToken)
    {
        List<string> keys = [];
        foreach (TokenPermission entry in GitHub)
        {
            if (ProbedPermissionOf(entry) is not { } permission || !missing.Contains(permission))
            {
                continue;
            }

            string key = !isFineGrainedToken && GrantedByClassicAdminRole.Contains(permission)
                ? GitHubClassicAdminRole.Key
                : entry.Key;

            if (!keys.Contains(key, StringComparer.Ordinal))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>The labels of <paramref name="keys"/> in list order; keys the list no longer holds are skipped.</summary>
    internal static IReadOnlyList<string> LabelsOf(string providerType, IReadOnlyCollection<string> keys) =>
        For(providerType)
            .Append(GitHubClassicAdminRole)
            .Where(entry => keys.Contains(entry.Key, StringComparer.Ordinal))
            .Select(entry => entry.Label)
            .ToList();

    private static WritePermission? ProbedPermissionOf(TokenPermission entry) => entry.VerifiedBy switch
    {
        PermissionVerification.WriteProbe probe => probe.Permission,
        PermissionVerification.ReadProbe probe => probe.Permission,
        _ => null,
    };

    private static string ComputeVersion(string providerType, IReadOnlyList<TokenPermission> entries)
    {
        IEnumerable<string> verified = entries
            .Where(entry => entry.VerifiedBy.VersionToken is not null)
            .Select(entry => $"{entry.Key}={entry.VerifiedBy.VersionToken}");
        string canonical = $"{providerType.ToLowerInvariant()}\n{string.Join('\n', verified)}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"{providerType.ToLowerInvariant()}-{Convert.ToHexStringLower(hash)[..16]}";
    }
}
