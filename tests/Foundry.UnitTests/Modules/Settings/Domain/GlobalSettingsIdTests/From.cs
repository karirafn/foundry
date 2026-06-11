using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsGlobalSettingsIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        GlobalSettingsId id = GlobalSettingsId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }

    [Fact]
    public void WhenDefaultCalled_ReturnsWellKnownSingletonId()
    {
        // Arrange
        Guid expected = new("00000000-0000-0000-0000-000000000001");

        // Act
        GlobalSettingsId id = GlobalSettingsId.Default;

        // Assert
        id.Value.ShouldBe(expected);
    }
}
