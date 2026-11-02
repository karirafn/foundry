using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.HostHooksTests;

public sealed class ValueEquality
{
    private static HostHooks Create(string hostSettingsPath, params string[] excludedCommands) =>
        HostHooks.Create(hostSettingsPath, excludedCommands)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    [Fact]
    public void WhenPathAndExclusionsMatch_AreEqualWithSameHashCode()
    {
        // Arrange
        HostHooks left = Create("/home/user/.claude/settings.json", "notify");
        HostHooks right = Create("/home/user/.claude/settings.json", "notify");

        // Act
        bool equal = left.Equals(right);

        // Assert
        left.ShouldSatisfyAllConditions(
            () => equal.ShouldBeTrue(),
            () => left.GetHashCode().ShouldBe(right.GetHashCode()));
    }

    [Fact]
    public void WhenExclusionsDiffer_AreNotEqual()
    {
        // Arrange
        HostHooks left = Create("/home/user/.claude/settings.json", "notify");
        HostHooks right = Create("/home/user/.claude/settings.json", "lint");

        // Act
        bool equal = left.Equals(right);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void WhenPathsDiffer_AreNotEqual()
    {
        // Arrange
        HostHooks left = Create("/home/a/.claude/settings.json");
        HostHooks right = Create("/home/b/.claude/settings.json");

        // Act
        bool equal = left.Equals(right);

        // Assert
        equal.ShouldBeFalse();
    }
}
