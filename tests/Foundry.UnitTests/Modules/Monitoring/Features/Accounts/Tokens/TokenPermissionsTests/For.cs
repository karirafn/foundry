using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Tokens.TokenPermissionsTests;

public sealed class For
{
    [Fact]
    public void EveryWritePermission_HasExactlyOneGitHubEntry()
    {
        // Arrange
        IReadOnlyList<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitHub);

        // Act
        Dictionary<WritePermission, int> entriesPerPermission = Enum.GetValues<WritePermission>()
            .ToDictionary(
                permission => permission,
                permission => entries.Count(entry =>
                    entry.VerifiedBy is PermissionVerification.WriteProbe probe && probe.Permission == permission));

        // Assert
        entriesPerPermission.Values.ShouldAllBe(count => count == 1);
    }

    [Fact]
    public void EveryWriteProbeEntry_NamesADefinedWritePermission()
    {
        // Arrange
        IReadOnlyList<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitHub);

        // Act
        IEnumerable<WritePermission> probed = entries
            .Select(entry => entry.VerifiedBy)
            .OfType<PermissionVerification.WriteProbe>()
            .Select(probe => probe.Permission);

        // Assert
        probed.ShouldAllBe(permission => Enum.IsDefined(permission));
    }

    [Fact]
    public void WhenProviderIsGitHub_WriteProbesRunContentsThenIssuesThenPullRequests()
    {
        // Arrange

        // Act
        IReadOnlyList<WritePermission> probes = TokenPermissions.GitHubWriteProbes;

        // Assert
        probes.ShouldBe([WritePermission.Contents, WritePermission.Issues, WritePermission.PullRequests]);
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
    public void WhenProviderIsGitHub_TemplateQueryListsWritePermissionsInOrder()
    {
        // Arrange

        // Act
        string query = TokenPermissions.GitHubTemplateQuery;

        // Assert
        query.ShouldBe("contents=write&issues=write&pull_requests=write&workflows=write");
    }

    [Fact]
    public void WhenProviderIsGitLab_MinimumAccessLevelIsDeveloper()
    {
        // Arrange

        // Act
        int level = TokenPermissions.GitLabMinimumAccessLevel;

        // Assert
        level.ShouldBe(30);
    }

    [Fact]
    public void WhenProviderIsGitLab_RoleEntryIsNotShownInTheForm()
    {
        // Arrange
        IReadOnlyList<TokenPermission> entries = TokenPermissions.For(ProviderTypes.GitLab);

        // Act
        TokenPermission role = entries.Single(entry => entry.VerifiedBy is PermissionVerification.GitLabRole);

        // Assert
        role.ShownInForm.ShouldBeFalse();
    }

    [Fact]
    public void LabelOf_Contents_ReturnsTheEntryLabel()
    {
        // Arrange

        // Act
        string label = TokenPermissions.LabelOf(WritePermission.Contents);

        // Assert
        label.ShouldBe("Contents (read and write)");
    }

    [Fact]
    public void LabelOf_Issues_ReturnsTheEntryLabel()
    {
        // Arrange

        // Act
        string label = TokenPermissions.LabelOf(WritePermission.Issues);

        // Assert
        label.ShouldBe("Issues (read and write)");
    }

    [Fact]
    public void LabelOf_PullRequests_ReturnsTheEntryLabel()
    {
        // Arrange

        // Act
        string label = TokenPermissions.LabelOf(WritePermission.PullRequests);

        // Assert
        label.ShouldBe("Pull requests (read and write)");
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
