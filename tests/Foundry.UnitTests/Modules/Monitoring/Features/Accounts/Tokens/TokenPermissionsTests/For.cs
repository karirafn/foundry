using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Tokens.TokenPermissionsTests;

public sealed class For
{
    private static WritePermission? ProbedPermission(TokenPermission entry) => entry.VerifiedBy switch
    {
        PermissionVerification.WriteProbe probe => probe.Permission,
        PermissionVerification.ReadProbe probe => probe.Permission,
        _ => null,
    };

    [Fact]
    public void EveryWritePermission_HasExactlyOneGitHubEntry()
    {
        // Arrange
        IReadOnlyList<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitHub);

        // Act
        Dictionary<WritePermission, int> entriesPerPermission = Enum.GetValues<WritePermission>()
            .ToDictionary(
                permission => permission,
                permission => entries.Count(entry => ProbedPermission(entry) == permission));

        // Assert
        entriesPerPermission.Values.ShouldAllBe(count => count == 1);
    }

    [Fact]
    public void EveryProbeEntry_NamesADefinedWritePermission()
    {
        // Arrange
        IReadOnlyList<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitHub);

        // Act
        IEnumerable<WritePermission> probed = entries.Select(ProbedPermission).OfType<WritePermission>();

        // Assert
        probed.ShouldAllBe(permission => Enum.IsDefined(permission));
    }

    [Fact]
    public void EveryEntryKey_IsUniqueAcrossProvidersAndGitLabGrantLevels()
    {
        // Arrange
        IEnumerable<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitHub)
            .Concat(TokenPermissions.For(ProviderTypes.GitLab));

        // Act
        List<string> keys = entries
            .Select(entry => entry.VerifiedBy is PermissionVerification.GitLabGrant grant
                ? $"{entry.Key}@{grant.Level}"
                : entry.Key)
            .ToList();

        // Assert
        keys.ShouldBeUnique();
    }

    [Fact]
    public void WhenProviderIsGitHub_ProbesRunInPermissionListOrder()
    {
        // Arrange

        // Act
        IReadOnlyList<WritePermission> probes = TokenPermissions.GitHubProbes;

        // Assert
        probes.ShouldBe(
        [
            WritePermission.Contents,
            WritePermission.Issues,
            WritePermission.PullRequests,
            WritePermission.Administration,
            WritePermission.Webhooks,
            WritePermission.CommitStatuses,
            WritePermission.Actions,
        ]);
    }

    [Fact]
    public void WhenProviderIsGitHub_TemplateQueryListsEveryTemplateParameterInOrder()
    {
        // Arrange

        // Act
        string query = TokenPermissions.GitHubTemplateQuery;

        // Assert
        query.ShouldBe(
            "contents=write&issues=write&pull_requests=write&administration=write&repository_hooks=write"
            + "&statuses=read&actions=write&workflows=write");
    }

    [Fact]
    public void WhenProviderIsGitHub_MetadataIsAlwaysGrantedAndWorkflowsIsNotVerified()
    {
        // Arrange
        IReadOnlyList<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitHub);

        // Act
        TokenPermission metadata = entries.Single(entry => entry.Key == "metadata");
        TokenPermission workflows = entries.Single(entry => entry.Key == "workflows");

        // Assert
        metadata.VerifiedBy.ShouldBeOfType<PermissionVerification.AlwaysGranted>();
        workflows.VerifiedBy.ShouldBeOfType<PermissionVerification.NotVerified>();
    }

    [Fact]
    public void WhenProviderIsGitLab_MinimumAccessLevelIsMaintainer()
    {
        // Arrange

        // Act
        int level = TokenPermissions.GitLabMinimumAccessLevel;

        // Assert
        level.ShouldBe(40);
    }

    [Fact]
    public void WhenProviderIsGitHub_ActionsIsReadAndWriteVerifiedByAWriteProbe()
    {
        // Arrange
        IReadOnlyList<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitHub);

        // Act
        TokenPermission actions = entries.Single(entry => entry.Key == "actions");

        // Assert
        actions.ShouldSatisfyAllConditions(
            () => actions.Label.ShouldBe("Actions (read and write)"),
            () => actions.TemplateParameter.ShouldBe(new TemplateParameter("actions", "write")),
            () => actions.VerifiedBy.ShouldBe(new PermissionVerification.WriteProbe(WritePermission.Actions)));
    }

    [Fact]
    public void WhenProviderIsGitHub_RepositoryAccessIsListedFirstAndNotChecked()
    {
        // Arrange

        // Act
        TokenPermission first = TokenPermissions.For(ProviderTypes.GitHub)[0];

        // Assert
        first.ShouldSatisfyAllConditions(
            () => first.Label.ShouldBe("Repository access: Only select repositories"),
            () => first.TemplateParameter.ShouldBeNull(),
            () => first.VerifiedBy.ShouldBeOfType<PermissionVerification.NotChecked>(),
            () => first.VerifiedBy.WireName.ShouldBe("not-checked"));
    }

    [Fact]
    public void VersionFor_GitHub_DiffersFromTheListThatProbedActionsForReadOnly()
    {
        // Arrange
        // The version the GitHub list had while Actions was verified by a read probe.
        const string previousGitHubVersion = "github-580e55d623cbf659";

        // Act
        string version = TokenPermissions.VersionFor(ProviderTypes.GitHub);

        // Assert
        version.ShouldNotBe(previousGitHubVersion);
    }

    [Fact]
    public void VersionFor_DiffersBetweenProviders()
    {
        // Arrange

        // Act
        string gitHub = TokenPermissions.VersionFor(ProviderTypes.GitHub);
        string gitLab = TokenPermissions.VersionFor(ProviderTypes.GitLab);

        // Assert
        gitHub.ShouldNotBe(gitLab);
    }

    [Fact]
    public void CurrentVersions_HoldsEveryProvidersVersion()
    {
        // Arrange

        // Act
        IReadOnlySet<string> versions = TokenPermissions.CurrentVersions;

        // Assert
        versions.ShouldBe(
            [TokenPermissions.VersionFor(ProviderTypes.GitHub), TokenPermissions.VersionFor(ProviderTypes.GitLab)],
            ignoreOrder: true);
    }

    [Fact]
    public void GitHubMissingKeys_ReturnsEachMissingPermissionInListOrder()
    {
        // Arrange
        WritePermission[] missing = [WritePermission.Webhooks, WritePermission.Administration];

        // Act
        IReadOnlyList<string> keys = TokenPermissions.GitHubMissingKeys(missing);

        // Assert
        keys.ShouldBe(["administration", "repository_hooks"]);
    }

    [Fact]
    public void LabelsOf_ReturnsLabelsInListOrder()
    {
        // Arrange

        // Act
        IReadOnlyList<string> labels = TokenPermissions.LabelsOf(
            ProviderTypes.GitHub,
            ["repository_hooks", "administration"]);

        // Assert
        labels.ShouldBe(
            ["Administration (read and write)", "Webhooks (read and write)"]);
    }

    [Fact]
    public void LabelsOf_GitLabRoleKey_ReturnsMaintainerRole()
    {
        // Arrange

        // Act
        IReadOnlyList<string> labels = TokenPermissions.LabelsOf(ProviderTypes.GitLab, [TokenPermissions.GitLabRoleKey]);

        // Assert
        labels.ShouldBe(["Maintainer role"]);
    }

    [Fact]
    public void LabelsOf_SkipsKeysTheListNoLongerHolds()
    {
        // Arrange

        // Act
        IReadOnlyList<string> labels = TokenPermissions.LabelsOf(ProviderTypes.GitHub, ["retired", "contents"]);

        // Assert
        labels.ShouldBe(["Contents (read and write)"]);
    }

    [Fact]
    public void WhenProviderIsUnknown_ReturnsEmpty()
    {
        // Arrange

        // Act
        IReadOnlyList<TokenPermission> entries = TokenPermissions.For("bitbucket");

        // Assert
        entries.ShouldBeEmpty();
    }

    [Fact]
    public void VersionFor_GitLab_DiffersFromTheListThatAcceptedOnlySelectedProjects()
    {
        // Arrange
        // The version the GitLab list had while it refused tokens reaching all memberships.
        const string previousGitLabVersion = "gitlab-2209a9fd6e8ce809";

        // Act
        string version = TokenPermissions.VersionFor(ProviderTypes.GitLab);

        // Assert
        version.ShouldNotBe(previousGitLabVersion);
    }

    [Fact]
    public void LabelsOf_KeyHeldAtTwoGitLabLevels_ReturnsItsLabelOnce()
    {
        // Arrange

        // Act
        IReadOnlyList<string> labels = TokenPermissions.LabelsOf(ProviderTypes.GitLab, ["read_project", "read_user"]);

        // Assert
        labels.ShouldBe(["Project: Read", "User (System Access): Read"]);
    }

    [Fact]
    public void VersionFor_GitLab_DiffersFromTheListThatCheckedTheApiScope()
    {
        // Arrange
        // The version the GitLab list had while it required the legacy api scope.
        const string previousGitLabVersion = "gitlab-dca72eddf91357a8";

        // Act
        string version = TokenPermissions.VersionFor(ProviderTypes.GitLab);

        // Assert
        version.ShouldNotBe(previousGitLabVersion);
    }

    [Fact]
    public void WhenProviderIsGitLab_GrantRequirementsHoldEachLevelsPermissionIdentifiers()
    {
        // Arrange

        // Act
        GitLabGrantRequirements requirements = TokenPermissions.GitLabGrantRequirements;

        // Assert
        requirements.ShouldSatisfyAllConditions(
            () => requirements.Project.ShouldBe(
            [
                "read_project", "read_work_item", "create_work_item", "read_merge_request", "create_merge_request",
                "update_merge_request", "merge_merge_request", "read_pipeline", "read_job", "run_job", "read_webhook",
                "create_webhook", "update_webhook", "delete_webhook", "trigger_webhook", "read_branch", "create_branch",
                "delete_branch", "protect_branch", "read_protected_branch", "update_protected_branch", "read_repository",
                "read_commit", "download_code", "push_code",
            ]),
            () => requirements.User.ShouldBe(["read_personal_access_token", "read_user"]),
            () => requirements.UserWithAllMemberships.ShouldBe(["read_project"]),
            () => requirements.GroupAllowed.ShouldBe(["read_package"]));
    }

    [Fact]
    public void GitLabLabelsOrIdentifiers_LabelsKnownIdentifiersAndKeepsUnknownOnes()
    {
        // Arrange

        // Act
        IReadOnlyList<string> labels = TokenPermissions.GitLabLabelsOrIdentifiers(["test_webhook", "trigger_webhook"]);

        // Assert
        labels.ShouldBe(["test_webhook", "Webhook (Integrations): Trigger"]);
    }

    [Fact]
    public void GitLabBroadReachWireNames_MapsEachAccessToItsWireName()
    {
        // Arrange

        // Act
        IReadOnlyList<string> names = TokenPermissions.GitLabBroadReachWireNames(
            ["all_memberships", "personal_projects", "instance"]);

        // Assert
        names.ShouldBe(["all-projects", "personal-projects", "instance"]);
    }
}
