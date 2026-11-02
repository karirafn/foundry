using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.HostHooksTests;

public sealed class Default
{
    [Fact]
    public void WhenRead_HasNoPathAndNoExclusions()
    {
        // Arrange
        // Act
        HostHooks hostHooks = HostHooks.Default;

        // Assert
        hostHooks.ShouldSatisfyAllConditions(
            () => hostHooks.HostSettingsPath.ShouldBeEmpty(),
            () => hostHooks.ExcludedCommands.ShouldBeEmpty());
    }
}
