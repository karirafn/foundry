using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerPromptsTests;

public sealed class Default
{
    [Fact]
    public void WhenDefault_PassesCreateInvariants()
    {
        // Arrange
        WorkerPrompts defaults = WorkerPrompts.Default;

        // Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(
            defaults.SystemPromptTemplate,
            defaults.WorkerPromptTemplate,
            defaults.BranchNamingInstruction);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenDefault_SystemPromptTemplateMatchesWorkerOptionsDefault()
    {
        // Arrange & Act
        WorkerPrompts defaults = WorkerPrompts.Default;

        // Assert
        defaults.SystemPromptTemplate.ShouldBe(
            "You are implementing GitHub issue #{issueNumber}.\n\n{issueContent}\n\nBranch naming: {branchNamingInstruction}.");
    }

    [Fact]
    public void WhenDefault_WorkerPromptTemplateMatchesWorkerOptionsDefault()
    {
        // Arrange & Act
        WorkerPrompts defaults = WorkerPrompts.Default;

        // Assert
        defaults.WorkerPromptTemplate.ShouldBe(
            "Implement GitHub issue #{issueNumber}. Create a feature branch, make the changes, commit, and push to the remote. Open a pull request for your branch, or reuse the existing one if a pull request already exists.");
    }

    [Fact]
    public void WhenDefault_BranchNamingInstructionMatchesWorkerOptionsDefault()
    {
        // Arrange & Act
        WorkerPrompts defaults = WorkerPrompts.Default;

        // Assert
        defaults.BranchNamingInstruction.ShouldBe("Use conventional branch naming");
    }
}
