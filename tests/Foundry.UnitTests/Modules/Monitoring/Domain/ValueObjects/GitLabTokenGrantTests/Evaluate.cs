using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.GitLabTokenGrantTests;

public sealed class Evaluate
{
    private static readonly GitLabGrantRequirements Requirements = new(
        Project: ["read_project", "read_job", "run_job"],
        User: ["read_personal_access_token", "read_user"],
        UserWithAllMemberships: ["read_project"],
        GroupAllowed: ["read_package"]);

    // The permissions of the tokens created on gitlab.com on 2026-11-27, as GET /personal_access_tokens/self
    // reported them.
    private static readonly GitLabGrantRequirements SpikeRequirements = new(
        Project: ["read_project", "read_code"],
        User: ["read_personal_access_token", "read_user"],
        UserWithAllMemberships: ["read_project"],
        GroupAllowed: ["read_package"]);

    private static GitLabTokenGrantEntry ProjectEntry(long projectId, params string[] permissions) =>
        new(GitLabTokenGrantEntry.SelectedMemberships, permissions, projectId, GroupId: null);

    private static GitLabTokenGrantEntry UserEntry(params string[] permissions) =>
        new(GitLabTokenGrantEntry.User, permissions, ProjectId: null, GroupId: null);

    private static GitLabTokenGrantEntry GroupEntry(long groupId, params string[] permissions) =>
        new(GitLabTokenGrantEntry.SelectedMemberships, permissions, ProjectId: null, groupId);

    private static GitLabTokenGrantEntry AllMembershipsEntry(params string[] permissions) =>
        new("all_memberships", permissions, ProjectId: null, GroupId: null);

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
    public void WhenTheTokenReachesAllGroupsAndProjects_AsReportedByGitLab_IsConforming()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
        [
            AllMembershipsEntry("read_project", "read_code", "read_package"),
            UserEntry("read_personal_access_token", "read_user", "read_project"),
        ]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(SpikeRequirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.Conforming>();
    }

    [Fact]
    public void WhenTheTokenReachesOnlyPersonalProjects_AsReportedByGitLab_IsBroadReach()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
        [
            new("personal_projects", ["read_project", "read_code", "read_package"], ProjectId: null, GroupId: null),
            UserEntry("read_personal_access_token", "read_user"),
        ]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(SpikeRequirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.BroadReach(["personal_projects"])]);
    }

    [Fact]
    public void WhenTheTokenReachesOneSelectedProject_AsReportedByGitLab_IsConforming()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
        [
            ProjectEntry(45100939, "read_project", "read_code", "read_package"),
            UserEntry("read_personal_access_token", "read_user"),
        ]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(SpikeRequirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.Conforming>();
    }

    [Fact]
    public void WhenAProjectEntryCarriesReadPackage_IsConforming()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [ProjectEntry(7, "read_project", "read_job", "run_job", "read_package"), ConformingUser()]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.Conforming>();
    }

    [Fact]
    public void WhenTheAllMembershipsEntryCarriesExactlyTheProjectSet_IsConforming()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
        [
            AllMembershipsEntry("read_project", "read_job", "run_job"),
            UserEntry("read_personal_access_token", "read_user", "read_project"),
        ]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.Conforming>();
    }

    [Fact]
    public void WhenTheAllMembershipsEntryLacksAProjectPermission_IsMissingThatPermission()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
        [
            AllMembershipsEntry("read_project", "read_job"),
            UserEntry("read_personal_access_token", "read_user", "read_project"),
        ]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.Missing(["run_job"])]);
    }

    [Fact]
    public void WhenTheAllMembershipsEntryHoldsAPermissionOutsideTheProjectSet_IsNotAllowed()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
        [
            AllMembershipsEntry("read_project", "read_job", "run_job", "delete_project"),
            UserEntry("read_personal_access_token", "read_user", "read_project"),
        ]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.NotAllowed(["delete_project"])]);
    }

    [Fact]
    public void WhenTheTokenReachesAllMembershipsWithoutUserReadProject_IsMissingIt()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
            [AllMembershipsEntry("read_project", "read_job", "run_job"), ConformingUser()]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.Missing(["read_project"])]);
    }

    [Fact]
    public void WhenAnAllMembershipsEntryCarriesAnId_IsBroadReach()
    {
        // Arrange
        GitLabTokenGrant grant = GitLabTokenGrant.Create(
        [
            ConformingProject(7),
            ConformingUser(),
            new("all_memberships", ["read_project", "read_job", "run_job"], ProjectId: 9, GroupId: null),
        ]);

        // Act
        GitLabGrantEvaluation evaluation = grant.Evaluate(Requirements);

        // Assert
        evaluation.ShouldBeOfType<GitLabGrantEvaluation.NonConforming>().Findings
            .ShouldBe([new GitLabGrantFinding.BroadReach(["all_memberships"])]);
    }

    [Fact]
    public void ReachesAllMemberships_IsTrueOnlyWithAnAllMembershipsEntry()
    {
        // Arrange
        GitLabTokenGrant allMemberships = GitLabTokenGrant.Create(
            [AllMembershipsEntry("read_project"), ConformingUser()]);
        GitLabTokenGrant selected = GitLabTokenGrant.Create([ConformingProject(7), ConformingUser()]);

        // Act
        bool allReaches = allMemberships.ReachesAllMemberships;
        bool selectedReaches = selected.ReachesAllMemberships;

        // Assert
        allReaches.ShouldBeTrue();
        selectedReaches.ShouldBeFalse();
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
