using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.ClaudeCodeSettingsTests;

public sealed class Create
{
    [Fact]
    public void WhenModelHasSurroundingWhitespace_TrimsIt()
    {
        // Arrange
        ClaudeCodeSettingsBuilder builder = new ClaudeCodeSettingsBuilder().WithModel("  claude-opus-4  ");

        // Act
        Result<ClaudeCodeSettings> result = builder.Build();

        // Assert
        result.ShouldBeOfType<Result<ClaudeCodeSettings>.Success>().Value.Model.ShouldBe("claude-opus-4");
    }

    [Fact]
    public void WhenModelIsOnlyWhitespace_CollapsesToUnset()
    {
        // Arrange
        ClaudeCodeSettingsBuilder builder = new ClaudeCodeSettingsBuilder().WithModel("   ");

        // Act
        Result<ClaudeCodeSettings> result = builder.Build();

        // Assert
        result.ShouldBeOfType<Result<ClaudeCodeSettings>.Success>().Value.Model.ShouldBeEmpty();
    }

    [Fact]
    public void WhenModelIsExactlyMaxLength_ReturnsSuccess()
    {
        // Arrange
        ClaudeCodeSettingsBuilder builder = new ClaudeCodeSettingsBuilder()
            .WithModel(new string('m', ClaudeCodeSettings.MaxModelLength));

        // Act
        Result<ClaudeCodeSettings> result = builder.Build();

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenModelExceedsMaxLength_FailsWithModelTooLongCodeNamingTheCap()
    {
        // Arrange
        ClaudeCodeSettingsBuilder builder = new ClaudeCodeSettingsBuilder()
            .WithModel(new string('m', ClaudeCodeSettings.MaxModelLength + 1));

        // Act
        Result<ClaudeCodeSettings> result = builder.Build();

        // Assert
        Error error = result.ShouldBeOfType<Result<ClaudeCodeSettings>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(SettingsErrors.ModelTooLongCode),
            () => error.Message.ShouldContain("200"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bash")]
    [InlineData("Bash()")]
    [InlineData("bash(git push)")]
    [InlineData("Delete(foo)")]
    [InlineData("Bash(git push) extra")]
    [InlineData("Bash(git push)\n")]
    public void WhenDenyRuleIsMalformed_FailsWithInvalidDenyRuleCode(string rule)
    {
        // Arrange
        ClaudeCodeSettingsBuilder builder = new ClaudeCodeSettingsBuilder().WithAdditionalDenyRules([rule]);

        // Act
        Result<ClaudeCodeSettings> result = builder.Build();

        // Assert
        Error error = result.ShouldBeOfType<Result<ClaudeCodeSettings>.Failure>().Error;
        error.Code.ShouldBe(SettingsErrors.InvalidDenyRuleCode);
    }

    [Theory]
    [InlineData("Bash(git push:*)")]
    [InlineData("Edit(.github/**)")]
    [InlineData("Read(./secrets/**)")]
    [InlineData("Write(*.pem)")]
    [InlineData("MultiEdit(src/**)")]
    [InlineData("Glob(**/*.key)")]
    [InlineData("Grep(password)")]
    public void WhenDenyRuleIsWellFormed_ReturnsSuccess(string rule)
    {
        // Arrange
        ClaudeCodeSettingsBuilder builder = new ClaudeCodeSettingsBuilder().WithAdditionalDenyRules([rule]);

        // Act
        Result<ClaudeCodeSettings> result = builder.Build();

        // Assert
        result.ShouldBeOfType<Result<ClaudeCodeSettings>.Success>().Value.AdditionalDenyRules.ShouldBe([rule]);
    }

    [Fact]
    public void WhenDenyRulesRepeat_KeepsFirstOccurrenceInOrder()
    {
        // Arrange
        ClaudeCodeSettingsBuilder builder = new ClaudeCodeSettingsBuilder()
            .WithAdditionalDenyRules(["Bash(rm:*)", "Edit(a)", "Bash(rm:*)", "Read(b)", "Edit(a)"]);

        // Act
        Result<ClaudeCodeSettings> result = builder.Build();

        // Assert
        result.ShouldBeOfType<Result<ClaudeCodeSettings>.Success>().Value.AdditionalDenyRules
            .ShouldBe(["Bash(rm:*)", "Edit(a)", "Read(b)"]);
    }

    [Fact]
    public void WhenDenyRulesDifferOnlyByCase_KeepsBoth()
    {
        // Arrange
        ClaudeCodeSettingsBuilder builder = new ClaudeCodeSettingsBuilder()
            .WithAdditionalDenyRules(["Bash(Foo)", "Bash(foo)"]);

        // Act
        Result<ClaudeCodeSettings> result = builder.Build();

        // Assert
        result.ShouldBeOfType<Result<ClaudeCodeSettings>.Success>().Value.AdditionalDenyRules.Count.ShouldBe(2);
    }
}
