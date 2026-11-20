using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.DispatchPauseTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_UsesTheFixedId()
    {
        // Arrange & Act
        DispatchPause pause = DispatchPause.Create();

        // Assert
        pause.Id.ShouldBe(DispatchPauseId.Default);
    }

    [Fact]
    public void WhenCreated_UsageLimitResetsAtIsNull()
    {
        // Arrange & Act
        DispatchPause pause = DispatchPause.Create();

        // Assert
        pause.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_IsDispatchPausedIsFalse()
    {
        // Arrange & Act
        DispatchPause pause = DispatchPause.Create();

        // Assert
        pause.IsDispatchPaused.ShouldBeFalse();
    }
}
