using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.RepositoryRolesTests;

/// <summary>
/// Guards the role folders checked into <c>workers/roles/</c> against the roles and placeholders defined in code
/// (ADR 0095): a role without a folder, or a prompt using a placeholder Foundry does not fill, fails here rather than
/// at image build or dispatch.
/// </summary>
public sealed class RepositoryRoles
{
    private static readonly string RolesDirectory =
        Path.Combine(RepositoryRoot.WorkersDirectory, WorkerRoleCatalogReader.RolesFolder);

    public static TheoryData<string> RoleNames => [.. WorkerRole.All.Select(role => role.Name)];

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void EveryRoleDefinedInCode_HasAPluginManifestAndPromptsInTheRepository(string roleName)
    {
        // Arrange
        string roleDirectory = Path.Combine(RolesDirectory, roleName);

        // Assert
        roleDirectory.ShouldSatisfyAllConditions(
            () => File.Exists(Path.Combine(roleDirectory, WorkerRoleCatalogReader.PluginManifestPath)).ShouldBeTrue(),
            () => File.Exists(Path.Combine(roleDirectory, WorkerRoleCatalogReader.SystemPromptPath)).ShouldBeTrue(),
            () => File.Exists(Path.Combine(roleDirectory, WorkerRoleCatalogReader.TaskPromptPath)).ShouldBeTrue());
    }

    [Fact]
    public void TheSharedPluginLoadedForEveryRole_ExistsInTheRepository()
    {
        // Assert
        File.Exists(Path.Combine(RolesDirectory, WorkerRole.SharedFolder, WorkerRoleCatalogReader.PluginManifestPath))
            .ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void RoleSystemPrompt_UsesOnlyPlaceholdersFoundryFills(string roleName)
    {
        // Arrange
        string template = File.ReadAllText(Path.Combine(RolesDirectory, roleName, WorkerRoleCatalogReader.SystemPromptPath));

        // Act
        IReadOnlyList<string> placeholders = RolePromptPlaceholders.FindIn(template);

        // Assert
        placeholders.ShouldAllBe(placeholder => RolePromptPlaceholders.SystemPrompt.Contains(placeholder));
    }

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void RoleTaskPrompt_UsesOnlyPlaceholdersFoundryFillsAndRequiresTheIssueNumber(string roleName)
    {
        // Arrange
        string template = File.ReadAllText(Path.Combine(RolesDirectory, roleName, WorkerRoleCatalogReader.TaskPromptPath));

        // Act
        IReadOnlyList<string> placeholders = RolePromptPlaceholders.FindIn(template);

        // Assert
        placeholders.ShouldSatisfyAllConditions(
            () => placeholders.ShouldAllBe(placeholder => RolePromptPlaceholders.TaskPrompt.Contains(placeholder)),
            () => placeholders.ShouldContain(RolePromptPlaceholders.IssueNumber));
    }

    [Fact]
    public void TheRepositoryRoleCatalog_Loads()
    {
        // Act
        Result<WorkerRoleCatalogContent> result = WorkerRoleCatalogReader.ReadFromDirectory(RepositoryRoot.WorkersDirectory);

        // Assert
        WorkerRoleCatalogContent catalog = result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        catalog.Roles.Select(role => role.Role).ShouldBe(WorkerRole.All);
    }

    [Fact]
    public void TheImplementRole_HasNoVerdictSchema()
    {
        // Act
        WorkerRoleCatalogContent catalog = LoadRepositoryCatalog();

        // Assert
        catalog.Find(WorkerRole.Implement).ShouldNotBeNull().VerdictSchema.ShouldBeNull();
    }

    [Theory]
    [InlineData("- Write a failing test for each behaviour and run it to see it fail for the right reason.")]
    [InlineData("- Write the minimum code to make that test pass.")]
    [InlineData("- Refactor while the tests stay green.")]
    [InlineData("- Fix a bug by starting with a test that reproduces it.")]
    [InlineData("- Make no production change without a test that drove it.")]
    public void TheRenderedImplementRolePrompt_ContainsTheTestDrivenDevelopmentSection(string rule)
    {
        // Arrange
        WorkerRoleDefinition implement = LoadRepositoryCatalog().Find(WorkerRole.Implement).ShouldNotBeNull();

        // Act
        Result<string> result = SystemPromptBuilder.Build(
            42,
            implement.SystemPromptTemplate,
            new DispatchContext.Fresh("feat/42-title"),
            "https://api.github.com/repos/owner/repo/issues/42",
            timeoutMinutes: 120,
            installsDocker: false,
            installsDotnet: false);

        // Assert
        string prompt = result.ShouldBeOfType<Result<string>.Success>().Value;
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("Test-driven development:"),
            () => prompt.ShouldContain(rule),
            () => prompt.ShouldNotContain(RolePromptPlaceholders.IssueNumber),
            () => prompt.ShouldNotContain(RolePromptPlaceholders.IssueContent));
    }

    private static WorkerRoleCatalogContent LoadRepositoryCatalog() =>
        WorkerRoleCatalogReader.ReadFromDirectory(RepositoryRoot.WorkersDirectory)
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
}
