using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.HostHooksTests;

public sealed class Create
{
    private static HostHooks CreateValid(string hostSettingsPath, params string[] excludedCommands) =>
        HostHooks.Create(hostSettingsPath, excludedCommands)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    private static Error CreateInvalid(string hostSettingsPath, params string[] excludedCommands) =>
        HostHooks.Create(hostSettingsPath, excludedCommands)
            .ShouldBeOfType<Result<HostHooks>.Failure>().Error;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenPathIsBlank_ReturnsUnconfiguredPath(string hostSettingsPath)
    {
        // Arrange
        // Act
        HostHooks hostHooks = CreateValid(hostSettingsPath);

        // Assert
        hostHooks.HostSettingsPath.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/home/user/.claude/settings.json", "/home/user/.claude/settings.json")]
    [InlineData("  /home/user/.claude/settings.json  ", "/home/user/.claude/settings.json")]
    [InlineData("//home/./user/.claude/settings.json", "/home/user/.claude/settings.json")]
    [InlineData(@"C:\Users\me\.claude\settings.json", @"C:\Users\me\.claude\settings.json")]
    [InlineData("C:/Users/me/.claude/settings.json", "C:/Users/me/.claude/settings.json")]
    public void WhenPathIsAbsolute_ReturnsCanonicalPath(string hostSettingsPath, string expected)
    {
        // Arrange
        // Act
        HostHooks hostHooks = CreateValid(hostSettingsPath);

        // Assert
        hostHooks.HostSettingsPath.ShouldBe(expected);
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("~/.claude/settings.json")]
    [InlineData(@"Users\me\settings.json")]
    public void WhenPathIsRelative_FailsWithNotAbsoluteCode(string hostSettingsPath)
    {
        // Arrange
        // Act
        Error error = CreateInvalid(hostSettingsPath);

        // Assert
        error.Code.ShouldBe(SettingsErrors.HostSettingsPathNotAbsoluteCode);
    }

    [Theory]
    [InlineData("/home/user/../root/settings.json")]
    [InlineData(@"C:\Users\..\settings.json")]
    public void WhenPathContainsTraversal_FailsWithTraversalCode(string hostSettingsPath)
    {
        // Arrange
        // Act
        Error error = CreateInvalid(hostSettingsPath);

        // Assert
        error.Code.ShouldBe(SettingsErrors.HostSettingsPathTraversalCode);
    }

    [Theory]
    [InlineData("/etc/claude/settings.json")]
    [InlineData("/run/secrets/settings.json")]
    [InlineData("/var/run/docker.sock")]
    [InlineData("/proc/self/environ")]
    public void WhenPathIsSensitive_FailsWithSensitiveCode(string hostSettingsPath)
    {
        // Arrange
        // Act
        Error error = CreateInvalid(hostSettingsPath);

        // Assert
        error.Code.ShouldBe(SettingsErrors.SensitiveHostSettingsPathCode);
    }

    [Theory]
    [InlineData("/home/user/settings.json:/etc/passwd")]
    [InlineData("/home/user/settings\u0001.json")]
    public void WhenPathHasInvalidCharacters_FailsWithInvalidCharactersCode(string hostSettingsPath)
    {
        // Arrange
        // Act
        Error error = CreateInvalid(hostSettingsPath);

        // Assert
        error.Code.ShouldBe(SettingsErrors.InvalidHostSettingsPathCharactersCode);
    }

    [Fact]
    public void WhenPathExceedsMaxLength_FailsWithTooLongCode()
    {
        // Arrange
        string hostSettingsPath = "/" + new string('a', HostHooks.MaxHostSettingsPathLength);

        // Act
        Error error = CreateInvalid(hostSettingsPath);

        // Assert
        error.Code.ShouldBe(SettingsErrors.HostSettingsPathTooLongCode);
    }

    [Fact]
    public void WhenExclusionsHaveSurroundingWhitespaceAndDuplicates_TrimsAndDeduplicatesPreservingOrder()
    {
        // Arrange
        // Act
        HostHooks hostHooks = CreateValid(
            "/home/user/.claude/settings.json",
            "  bash ~/.claude/hooks/windows-check.sh ",
            "notify-send done",
            "bash ~/.claude/hooks/windows-check.sh");

        // Assert
        hostHooks.ExcludedCommands.ShouldBe(["bash ~/.claude/hooks/windows-check.sh", "notify-send done"]);
    }

    [Fact]
    public void WhenExclusionsDifferOnlyByCase_KeepsBoth()
    {
        // Arrange
        // Act
        HostHooks hostHooks = CreateValid(string.Empty, "Notify", "notify");

        // Assert
        hostHooks.ExcludedCommands.ShouldBe(["Notify", "notify"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenExclusionIsBlank_FailsWithEmptyExclusionCode(string excludedCommand)
    {
        // Arrange
        // Act
        Error error = CreateInvalid(string.Empty, "notify", excludedCommand);

        // Assert
        error.Code.ShouldBe(SettingsErrors.EmptyExcludedHookCommandCode);
    }

    [Fact]
    public void WhenExclusionExceedsMaxLength_FailsWithTooLongCode()
    {
        // Arrange
        string excludedCommand = new('x', HostHooks.MaxExcludedCommandLength + 1);

        // Act
        Error error = CreateInvalid(string.Empty, excludedCommand);

        // Assert
        error.Code.ShouldBe(SettingsErrors.ExcludedHookCommandTooLongCode);
    }

    [Fact]
    public void WhenTooManyExclusions_FailsWithTooManyCode()
    {
        // Arrange
        string[] excludedCommands = [.. Enumerable.Range(0, HostHooks.MaxExcludedCommandCount + 1).Select(i => $"hook-{i}")];

        // Act
        Error error = CreateInvalid(string.Empty, excludedCommands);

        // Assert
        error.Code.ShouldBe(SettingsErrors.TooManyExcludedHookCommandsCode);
    }

    [Fact]
    public void WhenExclusionsGivenWithoutPath_KeepsExclusions()
    {
        // Arrange
        // Act
        HostHooks hostHooks = CreateValid(string.Empty, "notify");

        // Assert
        hostHooks.ShouldSatisfyAllConditions(
            () => hostHooks.HostSettingsPath.ShouldBeEmpty(),
            () => hostHooks.ExcludedCommands.ShouldBe(["notify"]));
    }
}
