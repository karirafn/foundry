using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.SystemPromptBuilderTests;

public sealed class Build
{
    [Fact]
    public void WhenAllPlaceholdersProvided_SubstitutesAll()
    {
        // Arrange
        WorkerOptions options = new()
        {
            SystemPromptTemplate = "Issue {issueNumber}: {title}. Body: {body}. Branch: {branchNamingInstruction}.",
            BranchNamingInstruction = "Use conventional branch naming",
        };

        // Act
        string result = SystemPromptBuilder.Build(42, "Fix the bug", "Detailed description", options);

        // Assert
        result.ShouldBe("Issue 42: Fix the bug. Body: Detailed description. Branch: Use conventional branch naming.");
    }

    [Fact]
    public void WhenPlaceholderAbsentFromTemplate_LeavesMissingPlaceholderUntouched()
    {
        // Arrange
        WorkerOptions options = new()
        {
            SystemPromptTemplate = "Issue {issueNumber}: {title}.",
            BranchNamingInstruction = "Use conventional branch naming",
        };

        // Act
        string result = SystemPromptBuilder.Build(7, "Short title", "Some body", options);

        // Assert
        result.ShouldBe("Issue 7: Short title.");
    }

    [Fact]
    public void WhenDefaultTemplate_SubstitutesAllFourPlaceholders()
    {
        // Arrange
        WorkerOptions options = new();

        // Act
        string result = SystemPromptBuilder.Build(99, "My title", "My body", options);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldContain("99"),
            () => result.ShouldContain("My title"),
            () => result.ShouldContain("My body"),
            () => result.ShouldContain(options.BranchNamingInstruction));
    }
}
