using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.GitLabTokenGrantTests;

public sealed class Evaluate
{
    private static readonly GitLabGrantRequirements Requirements = new(
        Project: ["read_project", "read_job", "run_job"],
        User: ["read_personal_access_token", "read_user"],
        GroupAllowed: ["read_package"]);

    private static GitLabTokenGrantEntry ProjectEntry(long projectId, params string[] permissions) =>
        new(GitLabTokenGrantEntry.SelectedMemberships, permissions, projectId, GroupId: null);

    private static GitLabTokenGrantEntry UserEntry(params string[] permissions) =>
        new(GitLabTokenGrantEntry.User, permissions, ProjectId: null, GroupId: null);

    private static GitLabTokenGrantEntry GroupEntry(long groupId, params string[] permissions) =>
        new(GitLabTokenGrantEntry.SelectedMemberships, permissions, ProjectId: null, groupId);

    private static GitLabTokenGrantEntry ConformingProject(long projectId) =>
        ProjectEntry(projectId, "read_project", "read_job", "run_job");

    private static GitLabTokenGrantEntry ConformingUser() =>
        UserEntry("read_personal_access_token", "read_user");

    [Fact]
    public void WhenEveryEntryHoldsExactlyItsLevelsSet_IsConforming()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create([ConformingProject(7), ConformingUser()]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.Conforming>();
    }

    [Fact]
    public void WhenAProjectEntryLacksAProjectPermission_IsMissingThatPermission()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create([ProjectEntry(7, "read_project", "read_job"), ConformingUser()]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.Missing(["run_job"])]);
    }

    [Fact]
    public void WhenUserEntriesLackAUserPermission_IsMissingThatPermission()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create([ConformingProject(7), UserEntry("read_personal_access_token")]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.Missing(["read_user"])]);
    }

    [Fact]
    public void WhenUserPermissionsAreSplitAcrossUserEntries_IsConforming()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ConformingProject(7), UserEntry("read_personal_access_token"), UserEntry("read_user")]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.Conforming>();
    }

    [Fact]
    public void WhenAProjectEntryHoldsAPermissionOutsideTheProjectSet_IsNotAllowed()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ProjectEntry(7, "read_project", "read_job", "run_job", "delete_project"), ConformingUser()]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.NotAllowed(["delete_project"])]);
    }

    [Fact]
    public void WhenAUserEntryHoldsAPermissionOutsideTheUserSet_IsNotAllowed()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ConformingProject(7), UserEntry("read_personal_access_token", "read_user", "read_project")]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.NotAllowed(["read_project"])]);
    }

    [Fact]
    public void WhenAGroupEntryHoldsMoreThanReadPackage_IsNotAllowed()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ConformingProject(7), ConformingUser(), GroupEntry(3, "read_package", "read_project")]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.NotAllowed(["read_project"])]);
    }

    [Fact]
    public void WhenAGroupEntryCarriesReadPackage_IsConforming()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ConformingProject(7), ConformingUser(), GroupEntry(3, "read_package")]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.Conforming>();
    }

    [Fact]
    public void WhenProjectEntriesCoverProjectsFoundryDoesNotMonitor_IsConforming()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ConformingProject(7), ConformingProject(8), ConformingProject(9), ConformingUser()]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.Conforming>();
    }

    [Theory]
    [InlineData("all_memberships")]
    [InlineData("personal_projects")]
    [InlineData("instance")]
    public void WhenAnEntryReachesBeyondSelectedMemberships_IsBroadReach(string access)
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ConformingProject(7), ConformingUser(), new(access, ["read_project"], ProjectId: null, GroupId: null)]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.BroadReach([access])]);
    }

    [Fact]
    public void WhenSeveralRulesBreak_ReportsBroadReachNotAllowedAndMissingTogether()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
        [
            ProjectEntry(7, "read_project", "read_job", "test_webhook"),
            ConformingUser(),
            new("instance", ["read_project"], ProjectId: null, GroupId: null),
        ]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings.ShouldBe(
        [
            new GitLabGrantFinding.BroadReach(["instance"]),
            new GitLabGrantFinding.NotAllowed(["test_webhook"]),
            new GitLabGrantFinding.Missing(["run_job"]),
        ]);
    }

    [Fact]
    public void WhenAnEntryHasAnUnknownAccess_IsBroadReachWithThatAccess()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ConformingProject(7), ConformingUser(), new("organization", ["read_project"], ProjectId: null, GroupId: null)]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.BroadReach(["organization"])]);
    }

    [Fact]
    public void Covers_IsTrueOnlyForAProjectEntrysProjectId()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create([ConformingProject(7), ConformingUser(), GroupEntry(8, "read_package")]);

        // Act
        bool coversSeven = grant.Covers(7);
        bool coversGroupEight = grant.Covers(8);

        // Assert
        coversSeven.ShouldBeTrue();
        coversGroupEight.ShouldBeFalse();
    }
}
