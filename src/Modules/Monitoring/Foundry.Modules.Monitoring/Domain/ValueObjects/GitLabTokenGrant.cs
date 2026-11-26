namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// One entry of a GitLab fine-grained token's <c>granular_scopes</c>: the reach it grants
/// (<see cref="Access"/>), the permission identifiers it carries, and the project or group it targets.
/// </summary>
internal sealed record GitLabTokenGrantEntry(
    string Access,
    IReadOnlyList<string> Permissions,
    long? ProjectId,
    long? GroupId)
{
    internal const string SelectedMemberships = "selected_memberships";
    internal const string User = "user";

    internal bool IsProjectEntry => Access == SelectedMemberships && ProjectId is not null;

    internal bool IsGroupEntry => Access == SelectedMemberships && ProjectId is null && GroupId is not null;

    internal bool IsUserEntry => Access == User;

    public bool Equals(GitLabTokenGrantEntry? other) =>
        other is not null
        && Access == other.Access
        && ProjectId == other.ProjectId
        && GroupId == other.GroupId
        && Permissions.SequenceEqual(other.Permissions, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Access, ProjectId, GroupId, Permissions.Count);
}

/// <summary>
/// The permission identifiers each grant level must hold. <see cref="Project"/> and <see cref="User"/> are
/// required exactly; <see cref="GroupAllowed"/> is all a group entry may carry, and none of it is required.
/// </summary>
internal sealed record GitLabGrantRequirements(
    IReadOnlyList<string> Project,
    IReadOnlyList<string> User,
    IReadOnlyList<string> GroupAllowed);

/// <summary>What a GitLab fine-grained token holds, as <c>GET /personal_access_tokens/self</c> reports it.</summary>
internal sealed class GitLabTokenGrant
{
    private GitLabTokenGrant(IReadOnlyList<GitLabTokenGrantEntry> entries)
    {
        Entries = entries;
    }

    internal IReadOnlyList<GitLabTokenGrantEntry> Entries { get; }

    internal static GitLabTokenGrant Create(IEnumerable<GitLabTokenGrantEntry> entries) =>
        new(entries
            .Select(entry => entry with { Permissions = entry.Permissions.ToArray() })
            .ToArray());

    /// <summary>Whether a project entry targets <paramref name="projectId"/>.</summary>
    internal bool Covers(long projectId) =>
        Entries.Any(entry => entry.IsProjectEntry && entry.ProjectId == projectId);

    /// <summary>The project ids the token's project entries target, in grant order, each once.</summary>
    internal IReadOnlyList<long> ProjectIds =>
        Entries
            .Where(entry => entry.IsProjectEntry)
            .Select(entry => entry.ProjectId!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Checks the grant against <paramref name="requirements"/>: an entry reaching more than selected
    /// projects or groups is broad reach, a permission outside its level's set is not allowed, and a
    /// project entry or the user entries lacking a required permission is missing. Every finding that
    /// holds is reported, in that order.
    /// </summary>
    internal GitLabGrantEvaluation Evaluate(GitLabGrantRequirements requirements)
    {
        List<string> broadReach = [];
        List<string> notAllowed = [];
        HashSet<string> missingProject = new(StringComparer.Ordinal);
        HashSet<string> userHeld = new(StringComparer.Ordinal);

        foreach (GitLabTokenGrantEntry entry in Entries)
        {
            IReadOnlyList<string>? allowed = entry switch
            {
                { IsProjectEntry: true } => requirements.Project,
                { IsUserEntry: true } => requirements.User,
                { IsGroupEntry: true } => requirements.GroupAllowed,
                _ => null,
            };

            if (allowed is null)
            {
                AddOnce(broadReach, entry.Access);
                continue;
            }

            foreach (string permission in entry.Permissions.Where(p => !allowed.Contains(p, StringComparer.Ordinal)))
            {
                AddOnce(notAllowed, permission);
            }

            if (entry.IsProjectEntry)
            {
                missingProject.UnionWith(requirements.Project.Except(entry.Permissions, StringComparer.Ordinal));
            }
            else if (entry.IsUserEntry)
            {
                userHeld.UnionWith(entry.Permissions);
            }
        }

        List<string> missing =
        [
            .. requirements.Project.Where(missingProject.Contains),
            .. requirements.User.Where(permission => !userHeld.Contains(permission)),
        ];

        List<GitLabGrantFinding> findings = [];
        if (broadReach.Count > 0)
        {
            findings.Add(new GitLabGrantFinding.BroadReach(broadReach));
        }

        if (notAllowed.Count > 0)
        {
            findings.Add(new GitLabGrantFinding.NotAllowed(notAllowed));
        }

        if (missing.Count > 0)
        {
            findings.Add(new GitLabGrantFinding.Missing(missing));
        }

        return findings.Count == 0
            ? new GitLabGrantEvaluation.Conforming()
            : new GitLabGrantEvaluation.NonConforming(findings);
    }

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }
}

/// <summary>Whether a <see cref="GitLabTokenGrant"/> holds exactly what Foundry requires.</summary>
internal abstract record GitLabGrantEvaluation
{
    private GitLabGrantEvaluation() { }

    internal sealed record Conforming : GitLabGrantEvaluation;

    /// <summary>At least one finding holds; every one that holds is listed.</summary>
    internal sealed record NonConforming(IReadOnlyList<GitLabGrantFinding> Findings) : GitLabGrantEvaluation
    {
        public bool Equals(NonConforming? other) =>
            other is not null && Findings.SequenceEqual(other.Findings);

        public override int GetHashCode() => Findings.Count;
    }

    internal IReadOnlyList<string> Missing => FindingOf<GitLabGrantFinding.Missing>()?.Keys ?? [];

    internal IReadOnlyList<string> NotAllowed => FindingOf<GitLabGrantFinding.NotAllowed>()?.Keys ?? [];

    internal IReadOnlyList<string> BroadReach => FindingOf<GitLabGrantFinding.BroadReach>()?.Access ?? [];

    private T? FindingOf<T>()
        where T : GitLabGrantFinding =>
        this is NonConforming nonConforming ? nonConforming.Findings.OfType<T>().FirstOrDefault() : null;
}

/// <summary>One way a <see cref="GitLabTokenGrant"/> departs from Foundry's requirements.</summary>
internal abstract record GitLabGrantFinding
{
    private GitLabGrantFinding() { }

    /// <summary>Required permission identifiers no entry of their level carries.</summary>
    internal sealed record Missing(IReadOnlyList<string> Keys) : GitLabGrantFinding
    {
        public bool Equals(Missing? other) => other is not null && Keys.SequenceEqual(other.Keys, StringComparer.Ordinal);

        public override int GetHashCode() => Keys.Count;
    }

    /// <summary>Permission identifiers an entry carries outside its level's set.</summary>
    internal sealed record NotAllowed(IReadOnlyList<string> Keys) : GitLabGrantFinding
    {
        public bool Equals(NotAllowed? other) => other is not null && Keys.SequenceEqual(other.Keys, StringComparer.Ordinal);

        public override int GetHashCode() => Keys.Count;
    }

    /// <summary>The <c>access</c> values of entries reaching more than selected projects or groups.</summary>
    internal sealed record BroadReach(IReadOnlyList<string> Access) : GitLabGrantFinding
    {
        public bool Equals(BroadReach? other) => other is not null && Access.SequenceEqual(other.Access, StringComparer.Ordinal);

        public override int GetHashCode() => Access.Count;
    }
}
