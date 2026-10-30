using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerPromptsTests;

public sealed class Create
{
    private const string ValidSystem = "You are implementing #{issueNumber}.";
    private const string ValidWorker = "Implement #{issueNumber}. Create a branch.";
    private const string ValidBranch = "Use conventional branch naming";

    [Fact]
    public void WhenAllInputsAreValid_ReturnsSuccess()
    {
        // Arrange & Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(ValidSystem, ValidWorker, ValidBranch);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenAllInputsAreValid_ReturnsValueWithCorrectFields()
    {
        // Arrange & Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(ValidSystem, ValidWorker, ValidBranch);

        // Assert
        WorkerPrompts prompts = result.ShouldBeOfType<Result<WorkerPrompts>.Success>().Value;
        prompts.ShouldSatisfyAllConditions(
            () => prompts.SystemPromptTemplate.ShouldBe(ValidSystem),
            () => prompts.WorkerPromptTemplate.ShouldBe(ValidWorker),
            () => prompts.BranchNamingInstruction.ShouldBe(ValidBranch));
    }

    [Fact]
    public void WhenSystemTemplateIsEmpty_ReturnsFailure()
    {
        // Arrange & Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(string.Empty, ValidWorker, ValidBranch);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerTemplateIsEmpty_ReturnsFailure()
    {
        // Arrange & Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(ValidSystem, string.Empty, ValidBranch);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenSystemTemplateExceedsMaxLength_ReturnsFailure()
    {
        // Arrange
        string tooLong = new('a', WorkerPrompts.MaxPromptTemplateLength + 1);

        // Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(tooLong, ValidWorker, ValidBranch);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerTemplateExceedsMaxLength_ReturnsFailure()
    {
        // Arrange
        string tooLong = new('a', WorkerPrompts.MaxPromptTemplateLength + 1);

        // Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(ValidSystem, tooLong, ValidBranch);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenSystemTemplateIsAtMaxLength_ReturnsSuccess()
    {
        // Arrange
        string atMax = new('a', WorkerPrompts.MaxPromptTemplateLength);

        // Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(atMax, ValidWorker, ValidBranch);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerTemplateIsAtMaxLength_ReturnsSuccess()
    {
        // Arrange
        string atMax = $"{new('a', WorkerPrompts.MaxPromptTemplateLength - "{issueNumber}".Length)}{{issueNumber}}";

        // Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(ValidSystem, atMax, ValidBranch);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerTemplateMissingIssueNumber_ReturnsFailure()
    {
        // Arrange & Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(ValidSystem, "Implement the issue.", ValidBranch);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\0")]
    [InlineData("prefix\nsuffix")]
    [InlineData("prefix\rsuffix")]
    [InlineData("prefix\0suffix")]
    public void WhenBranchInstructionContainsControlChar_ReturnsFailure(string branchInstruction)
    {
        // Arrange & Act
        Result<WorkerPrompts> result = WorkerPrompts.Create(ValidSystem, ValidWorker, branchInstruction);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
