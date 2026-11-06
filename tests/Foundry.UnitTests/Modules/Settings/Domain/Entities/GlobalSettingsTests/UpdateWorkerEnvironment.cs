using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateWorkerEnvironment
{
    private const int CustomMemoryLimitMb = 4096;
    private const double CustomCpuLimit = 1.5;
    private const int CustomPidsLimit = 256;

    private static WorkerResourceLimits CreateLimits() =>
        WorkerResourceLimits.Create(CustomMemoryLimitMb, CustomCpuLimit, CustomPidsLimit)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    [Fact]
    public void WhenCreated_ExposesDefaultLimits()
    {
        // Arrange
        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.WorkerResourceLimits.ShouldBe(WorkerResourceLimits.Default);
    }

    [Fact]
    public void WhenLimitsChanged_ReturnsTrueAndSetsLimits()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        WorkerResourceLimits limits = CreateLimits();

        // Act
        bool changed = settings.UpdateWorkerEnvironment(limits);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => changed.ShouldBeTrue(),
            () => settings.WorkerResourceLimits.ShouldBe(limits));
    }

    [Fact]
    public async Task WhenLimitsChanged_BumpsUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;
        await Task.Delay(5, TestContext.Current.CancellationToken);

        // Act
        settings.UpdateWorkerEnvironment(CreateLimits());

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThan(before);
    }

    [Fact]
    public void WhenLimitsEqualCurrent_ReturnsFalseAndLeavesUpdatedAtUnchanged()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        bool changed = settings.UpdateWorkerEnvironment(WorkerResourceLimits.Default);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => changed.ShouldBeFalse(),
            () => settings.UpdatedAt.ShouldBe(before));
    }
}
