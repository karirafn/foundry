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
    public void EveryEntryKey_IsUniqueAcrossProvidersAndTheClassicAdminRole()
    {
        // Arrange
        IEnumerable<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitHub)
            .Concat(TokenPermissions.For(ProviderTypes.GitLab))
            .Append(TokenPermissions.GitHubClassicAdminRole);

        // Act
        List<string> keys = entries.Select(entry => entry.Key).ToList();

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
    public void WhenProviderIsGitHub_ClassicScopesAreRepo()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = TokenPermissions.GitHubClassicScopes;

        // Assert
        scopes.ShouldBe(["repo"]);
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
            + "&statuses=read&actions=read&workflows=write");
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
    public void GitHubMissingKeys_ForAFineGrainedToken_ReturnsEachMissingPermissionInListOrder()
    {
        // Arrange
        WritePermission[] missing = [WritePermission.Webhooks, WritePermission.Administration];

        // Act
        IReadOnlyList<string> keys = TokenPermissions.GitHubMissingKeys(missing, isFineGrainedToken: true);

        // Assert
        keys.ShouldBe(["administration", "repository_hooks"]);
    }

    [Fact]
    public void GitHubMissingKeys_ForAClassicToken_ReportsAdministrationAndWebhooksAsTheAdminRole()
    {
        // Arrange
        WritePermission[] missing = [WritePermission.Administration, WritePermission.Webhooks];

        // Act
        IReadOnlyList<string> keys = TokenPermissions.GitHubMissingKeys(missing, isFineGrainedToken: false);

        // Assert
        keys.ShouldBe(["admin_role"]);
    }

    [Fact]
    public void GitHubMissingKeys_ForAClassicToken_KeepsOtherPermissions()
    {
        // Arrange
        WritePermission[] missing = [WritePermission.Contents, WritePermission.Webhooks];

        // Act
        IReadOnlyList<string> keys = TokenPermissions.GitHubMissingKeys(missing, isFineGrainedToken: false);

        // Assert
        keys.ShouldBe(["contents", "admin_role"]);
    }

    [Fact]
    public void LabelsOf_ReturnsLabelsInListOrder()
    {
        // Arrange

        // Act
        IReadOnlyList<string> labels = TokenPermissions.LabelsOf(
            ProviderTypes.GitHub,
            ["repository_hooks", "administration", "admin_role"]);

        // Assert
        labels.ShouldBe(
            ["Administration (read and write)", "Webhooks (read and write)", "Admin role on the repository"]);
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
}
