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

    private static WorkerMounts CreateMounts() =>
        WorkerMounts.Create([new WorkerMount("/data", "/srv/data", true)])
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    [Fact]
    public void WhenCreated_ExposesDefaultLimitsAndMounts()
    {
        // Arrange
        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => settings.WorkerResourceLimits.ShouldBe(WorkerResourceLimits.Default),
            () => settings.WorkerMounts.ShouldBe(WorkerMounts.Default));
    }

    [Fact]
    public void WhenValuesChanged_ReturnsTrueAndSetsBoth()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        WorkerResourceLimits limits = CreateLimits();
        WorkerMounts mounts = CreateMounts();

        // Act
        bool changed = settings.UpdateWorkerEnvironment(limits, mounts);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => changed.ShouldBeTrue(),
            () => settings.WorkerResourceLimits.ShouldBe(limits),
            () => settings.WorkerMounts.ShouldBe(mounts));
    }

    [Fact]
    public void WhenOnlyMountsChange_ReturnsTrue()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        bool changed = settings.UpdateWorkerEnvironment(WorkerResourceLimits.Default, CreateMounts());

        // Assert
        changed.ShouldBeTrue();
    }

    [Fact]
    public void WhenOnlyLimitsChange_ReturnsTrue()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        bool changed = settings.UpdateWorkerEnvironment(CreateLimits(), WorkerMounts.Default);

        // Assert
        changed.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenValuesChanged_BumpsUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;
        await Task.Delay(5, TestContext.Current.CancellationToken);

        // Act
        settings.UpdateWorkerEnvironment(CreateLimits(), CreateMounts());

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThan(before);
    }

    [Fact]
    public void WhenValuesEqualCurrent_ReturnsFalseAndLeavesUpdatedAtUnchanged()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;
        WorkerMounts equalMounts = WorkerMounts.Create([])
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        // Act
        bool changed = settings.UpdateWorkerEnvironment(WorkerResourceLimits.Default, equalMounts);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => changed.ShouldBeFalse(),
            () => settings.UpdatedAt.ShouldBe(before));
    }
}
