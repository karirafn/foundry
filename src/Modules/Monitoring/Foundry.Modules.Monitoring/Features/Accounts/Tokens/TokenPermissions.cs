using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

    /// <summary>
    /// Verified from a GitLab fine-grained token's grant: <paramref name="Level"/>'s entries must carry the
    /// permission, which GitLab's form shows as <paramref name="Action"/> on <paramref name="Resource"/>.
    /// </summary>
    internal sealed record GitLabGrant(GitLabGrantLevel Level, string Resource, string Action) : PermissionVerification
    {
        internal override string VersionToken => $"gitlab-grant:{Level}";
        internal override string WireName => "granted";
    }

    /// <summary>Verified by a project entry of the GitLab token's grant targeting the repository's project.</summary>
    internal sealed record GitLabSelectedProject : PermissionVerification
    {
        internal override string VersionToken => "gitlab-selected-project";
        internal override string WireName => "granted";
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

    /// <summary>Required, but Foundry has no way to check it.</summary>
    internal sealed record NotChecked : PermissionVerification
    {
        internal override string? VersionToken => null;
        internal override string WireName => "not-checked";
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
/// <param name="VerifiedBy">How Foundry checks the token holds it.</param>
/// <param name="Group">The heading the requirements list shows it under, or <see langword="null"/> for none.</param>
internal sealed record TokenPermission(
    string Key,
    string Label,
    TemplateParameter? TemplateParameter,
    PermissionVerification VerifiedBy,
    string? Group = null);

/// <summary>Which entries of a GitLab fine-grained token's grant carry a permission.</summary>
internal enum GitLabGrantLevel
{
    /// <summary>Each project entry, one per selected project; required.</summary>
    Project,

    /// <summary>The user entries; required.</summary>
    User,

    /// <summary>The user entries; required only when the token reaches all memberships, and not allowed otherwise.</summary>
    UserWithAllMemberships,

    /// <summary>A group, project or all-memberships entry; allowed, never required.</summary>
    Group,
}

/// <summary>
/// The one list per provider of what an account token needs. The account form, the GitHub creation
/// link, the probes, the GitLab minimum role and the permission-list version all derive from it, so a
/// permission is added in one place (ADR 0115).
/// </summary>
internal static class TokenPermissions
{
    private const string WriteLevel = "write";
    private const string ReadLevel = "read";
    private const int GitLabMaintainerAccessLevel = 40;

    private static readonly IReadOnlyList<TokenPermission> GitHub =
    [
        // GitHub gives a fine-grained token no way to read which repositories it reaches.
        new(
            "repository_access",
            "Repository access: Only select repositories",
            TemplateParameter: null,
            new PermissionVerification.NotChecked()),
        new(
            "contents",
            "Contents (read and write)",
            new TemplateParameter("contents", WriteLevel),
            new PermissionVerification.WriteProbe(WritePermission.Contents)),
        new(
            "issues",
            "Issues (read and write)",
            new TemplateParameter("issues", WriteLevel),
            new PermissionVerification.WriteProbe(WritePermission.Issues)),
        new(
            "pull_requests",
            "Pull requests (read and write)",
            new TemplateParameter("pull_requests", WriteLevel),
            new PermissionVerification.WriteProbe(WritePermission.PullRequests)),
        new(
            "administration",
            "Administration (read and write)",
            new TemplateParameter("administration", WriteLevel),
            new PermissionVerification.WriteProbe(WritePermission.Administration)),
        new(
            "repository_hooks",
            "Webhooks (read and write)",
            new TemplateParameter("repository_hooks", WriteLevel),
            new PermissionVerification.WriteProbe(WritePermission.Webhooks)),
        new(
            "statuses",
            "Commit statuses (read)",
            new TemplateParameter("statuses", ReadLevel),
            new PermissionVerification.ReadProbe(WritePermission.CommitStatuses)),
        new(
            "actions",
            "Actions (read and write)",
            new TemplateParameter("actions", WriteLevel),
            new PermissionVerification.WriteProbe(WritePermission.Actions)),
        new(
            "metadata",
            "Metadata (read)",
            TemplateParameter: null,
            new PermissionVerification.AlwaysGranted()),
        new(
            "workflows",
            "Workflows (read and write)",
            new TemplateParameter("workflows", WriteLevel),
            new PermissionVerification.NotVerified()),
    ];

    private const string GitLabAccessGroup = "Group and project access";
    private const string GitLabProjectGroup = "On each selected project, or on all groups and projects";
    private const string GitLabUserGroup = "On your user";
    private const string GitLabUserWithAllMembershipsGroup = "On your user, only with all groups and projects";
    private const string GitLabPackageGroup = "Optional, for a GitLab-hosted NuGet feed";
    private const string GitLabRoleGroup = "Repository role";

    // Keys are GitLab's permission identifiers as granular_scopes[].permissions reports them. The set
    // covers every GitLab call Foundry makes plus the Publisher's git fetch and push.
    private static readonly IReadOnlyList<TokenPermission> GitLab =
    [
        new(
            "selected_projects",
            "All groups and projects I have access to, or only selected projects (select each monitored project individually)",
            TemplateParameter: null,
            new PermissionVerification.GitLabSelectedProject(),
            GitLabAccessGroup),
        GitLabPermission("read_project", GitLabGrantLevel.Project, "Project", "Read"),
        GitLabPermission("read_work_item", GitLabGrantLevel.Project, "Work Item (Project Planning)", "Read"),
        GitLabPermission("create_work_item", GitLabGrantLevel.Project, "Work Item (Project Planning)", "Create"),
        GitLabPermission("read_merge_request", GitLabGrantLevel.Project, "Merge Request (Repository)", "Read"),
        GitLabPermission("create_merge_request", GitLabGrantLevel.Project, "Merge Request (Repository)", "Create"),
        GitLabPermission("update_merge_request", GitLabGrantLevel.Project, "Merge Request (Repository)", "Update"),
        GitLabPermission("merge_merge_request", GitLabGrantLevel.Project, "Merge Request (Repository)", "Merge"),
        GitLabPermission("read_pipeline", GitLabGrantLevel.Project, "Pipeline (CI/CD)", "Read"),
        GitLabPermission("read_job", GitLabGrantLevel.Project, "Job (CI/CD)", "Read"),
        GitLabPermission("run_job", GitLabGrantLevel.Project, "Job (CI/CD)", "Run"),
        GitLabPermission("read_webhook", GitLabGrantLevel.Project, "Webhook (Integrations)", "Read"),
        GitLabPermission("create_webhook", GitLabGrantLevel.Project, "Webhook (Integrations)", "Create"),
        GitLabPermission("update_webhook", GitLabGrantLevel.Project, "Webhook (Integrations)", "Update"),
        GitLabPermission("delete_webhook", GitLabGrantLevel.Project, "Webhook (Integrations)", "Delete"),
        GitLabPermission("trigger_webhook", GitLabGrantLevel.Project, "Webhook (Integrations)", "Trigger"),
        GitLabPermission("read_branch", GitLabGrantLevel.Project, "Branch", "Read"),
        GitLabPermission("create_branch", GitLabGrantLevel.Project, "Branch", "Create"),
        GitLabPermission("delete_branch", GitLabGrantLevel.Project, "Branch", "Delete"),
        GitLabPermission("protect_branch", GitLabGrantLevel.Project, "Branch", "Protect"),
        GitLabPermission("read_protected_branch", GitLabGrantLevel.Project, "Protected Branch", "Read"),
        GitLabPermission("update_protected_branch", GitLabGrantLevel.Project, "Protected Branch", "Update"),
        GitLabPermission("read_repository", GitLabGrantLevel.Project, "Repository", "Read"),
        GitLabPermission("read_commit", GitLabGrantLevel.Project, "Commit", "Read"),
        GitLabPermission("download_code", GitLabGrantLevel.Project, "Code", "Download"),
        GitLabPermission("push_code", GitLabGrantLevel.Project, "Code", "Push"),
        GitLabPermission(GitLabReadOwnTokenKey, GitLabGrantLevel.User, "Personal Access Token (System Access)", "Read"),
        GitLabPermission("read_user", GitLabGrantLevel.User, "User (System Access)", "Read"),
        GitLabPermission("read_project", GitLabGrantLevel.UserWithAllMemberships, "Project", "Read"),
        GitLabPermission("read_package", GitLabGrantLevel.Group, "Package (Packages and Registry)", "Read"),
        new(
            "maintainer_role",
            "Maintainer role",
            TemplateParameter: null,
            new PermissionVerification.GitLabRole(GitLabMaintainerAccessLevel),
            GitLabRoleGroup),
    ];

    /// <summary>The key of the permission a GitLab token needs to read its own grant.</summary>
    internal const string GitLabReadOwnTokenKey = "read_personal_access_token";

    private static TokenPermission GitLabPermission(string key, GitLabGrantLevel level, string resource, string action) =>
        new(
            key,
            $"{resource}: {action}",
            TemplateParameter: null,
            new PermissionVerification.GitLabGrant(level, resource, action),
            level switch
            {
                GitLabGrantLevel.Project => GitLabProjectGroup,
                GitLabGrantLevel.User => GitLabUserGroup,
                GitLabGrantLevel.UserWithAllMemberships => GitLabUserWithAllMembershipsGroup,
                _ => GitLabPackageGroup,
            });

    private static readonly FrozenDictionary<string, IReadOnlyList<TokenPermission>> ByProvider =
        new Dictionary<string, IReadOnlyList<TokenPermission>>
        {
            [ProviderTypes.GitHub] = GitHub,
            [ProviderTypes.GitLab] = GitLab,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<TokenPermission> For(string providerType) =>
        ByProvider.TryGetValue(providerType, out IReadOnlyList<TokenPermission>? entries) ? entries : [];

    /// <summary>
    /// What the account form and setup wizard list for <paramref name="providerType"/>, in list order.
    /// Consecutive GitLab grant permissions on one resource share a row, such as <c>Code: Download, Push</c>.
    /// </summary>
    internal static IReadOnlyList<TokenPermissionRequirement> RequirementsFor(string providerType)
    {
        List<TokenPermissionRequirement> rows = [];
        PermissionVerification.GitLabGrant? previous = null;

        foreach (TokenPermission entry in For(providerType))
        {
            PermissionVerification.GitLabGrant? grant = entry.VerifiedBy as PermissionVerification.GitLabGrant;
            if (grant is not null && previous is not null
                && previous.Resource == grant.Resource && previous.Level == grant.Level)
            {
                rows[^1] = rows[^1] with { Label = $"{rows[^1].Label}, {grant.Action}" };
            }
            else
            {
                rows.Add(new TokenPermissionRequirement(entry.Label, entry.VerifiedBy.WireName, entry.Group));
            }

            previous = grant;
        }

        return rows;
    }

    /// <summary>The GitHub probes, write and read, in the order they run.</summary>
    internal static IReadOnlyList<WritePermission> GitHubProbes { get; } = GitHub
        .Select(ProbedPermissionOf)
        .OfType<WritePermission>()
        .ToList();

    /// <summary>The creation-URL query that preselects every GitHub permission with a template parameter.</summary>
    internal static string GitHubTemplateQuery { get; } = string.Join(
        '&',
        GitHub
            .Select(entry => entry.TemplateParameter)
            .OfType<TemplateParameter>()
            .Select(parameter => parameter.ToString()));

    /// <summary>The permission identifiers each level of a GitLab token's grant must, or may, carry.</summary>
    internal static GitLabGrantRequirements GitLabGrantRequirements { get; } = new(
        Project: GitLabGrantKeys(GitLabGrantLevel.Project),
        User: GitLabGrantKeys(GitLabGrantLevel.User),
        UserWithAllMemberships: GitLabGrantKeys(GitLabGrantLevel.UserWithAllMemberships),
        GroupAllowed: GitLabGrantKeys(GitLabGrantLevel.Group));

    /// <summary>The key a GitLab <c>Denied</c> verdict stores for a repository no project entry targets.</summary>
    internal static string GitLabSelectedProjectKey { get; } = GitLab
        .Single(entry => entry.VerifiedBy is PermissionVerification.GitLabSelectedProject)
        .Key;

    /// <summary>
    /// The label of each GitLab permission identifier in <paramref name="identifiers"/>, in their order; an
    /// identifier the list does not hold is shown as GitLab sent it.
    /// </summary>
    internal static IReadOnlyList<string> GitLabLabelsOrIdentifiers(IEnumerable<string> identifiers) =>
        identifiers
            .Select(identifier => GitLab.FirstOrDefault(entry =>
                    entry.VerifiedBy is PermissionVerification.GitLabGrant
                    && string.Equals(entry.Key, identifier, StringComparison.Ordinal))?.Label
                ?? identifier)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The wire names of broad-reach <c>access</c> values: <c>all-projects</c>, <c>personal-projects</c> and
    /// <c>instance</c>; a value GitLab adds later is sent as GitLab sent it.
    /// </summary>
    internal static IReadOnlyList<string> GitLabBroadReachWireNames(IEnumerable<string> access) =>
        access
            .Select(value => value switch
            {
                "all_memberships" => "all-projects",
                "personal_projects" => "personal-projects",
                _ => value,
            })
            .ToList();

    private static List<string> GitLabGrantKeys(GitLabGrantLevel level) =>
        GitLab
            .Where(entry => entry.VerifiedBy is PermissionVerification.GitLabGrant grant && grant.Level == level)
            .Select(entry => entry.Key)
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

    /// <summary>The keys of the GitHub permissions a token lacks, in list order.</summary>
    internal static IReadOnlyList<string> GitHubMissingKeys(IReadOnlyCollection<WritePermission> missing) =>
        GitHub
            .Where(entry => ProbedPermissionOf(entry) is { } permission && missing.Contains(permission))
            .Select(entry => entry.Key)
            .ToList();

    /// <summary>
    /// The labels of <paramref name="keys"/> in list order, each once; keys the list no longer holds are skipped.
    /// </summary>
    internal static IReadOnlyList<string> LabelsOf(string providerType, IReadOnlyCollection<string> keys) =>
        For(providerType)
            .Where(entry => keys.Contains(entry.Key, StringComparer.Ordinal))
            .Select(entry => entry.Label)
            .Distinct(StringComparer.Ordinal)
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
