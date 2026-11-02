using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateHostHooks
{
    private static HostHooks CreateHostHooks() =>
        HostHooks.Create("/home/user/.claude/settings.json", ["notify"])
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    [Fact]
    public void WhenCreated_HasDefaultHostHooks()
    {
        // Arrange
        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.HostHooks.ShouldBe(HostHooks.Default);
    }

    [Fact]
    public void WhenValueChanged_ReturnsTrueAndSetsIt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        HostHooks hostHooks = CreateHostHooks();

        // Act
        bool changed = settings.UpdateHostHooks(hostHooks);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => changed.ShouldBeTrue(),
            () => settings.HostHooks.ShouldBe(hostHooks));
    }

    [Fact]
    public void WhenValueUnchanged_ReturnsFalseAndKeepsUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateHostHooks(CreateHostHooks());
        DateTimeOffset updatedAt = settings.UpdatedAt;

        // Act
        bool changed = settings.UpdateHostHooks(CreateHostHooks());

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => changed.ShouldBeFalse(),
            () => settings.UpdatedAt.ShouldBe(updatedAt));
    }
}
