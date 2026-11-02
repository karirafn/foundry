using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerOptionsTests;

public sealed class Defaults
{
    [Fact]
    public void WhenDefaultOptionsCreated_HasExpectedDefaults()
    {
        // Arrange
        // Act
        WorkerOptions options = new();

        // Assert
        options.ShouldSatisfyAllConditions(
            () => options.Settings.ShouldNotBeNull(),
            () => options.Settings.Hooks.ShouldBeEmpty());
    }
}
