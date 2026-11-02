using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WorkerResourceLimitsTests;

public sealed class Create
{
    [Fact]
    public void WhenAllInputsAreInRange_ExposesTheThreeValues()
    {
        // Arrange
        WorkerResourceLimitsBuilder builder = new WorkerResourceLimitsBuilder()
            .WithMemoryLimitMb(4096)
            .WithCpuLimit(1.5)
            .WithPidsLimit(256);

        // Act
        Result<WorkerResourceLimits> result = builder.Build();

        // Assert
        WorkerResourceLimits limits = result.ShouldBeOfType<Result<WorkerResourceLimits>.Success>().Value;
        limits.ShouldSatisfyAllConditions(
            () => limits.MemoryLimitMb.ShouldBe(4096),
            () => limits.CpuLimit.ShouldBe(1.5),
            () => limits.PidsLimit.ShouldBe(256));
    }

    [Theory]
    [InlineData(511)]
    [InlineData(65537)]
    public void WhenMemoryIsOutOfRange_FailsWithMemoryCodeNamingMinAndMax(int memoryLimitMb)
    {
        // Arrange
        WorkerResourceLimitsBuilder builder = new WorkerResourceLimitsBuilder().WithMemoryLimitMb(memoryLimitMb);

        // Act
        Result<WorkerResourceLimits> result = builder.Build();

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerResourceLimits>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(SettingsErrors.InvalidMemoryLimitCode),
            () => error.Message.ShouldContain("512"),
            () => error.Message.ShouldContain("65536"));
    }

    [Theory]
    [InlineData(0.49)]
    [InlineData(32.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void WhenCpuIsOutOfRange_FailsWithCpuCodeNamingMinAndMax(double cpuLimit)
    {
        // Arrange
        WorkerResourceLimitsBuilder builder = new WorkerResourceLimitsBuilder().WithCpuLimit(cpuLimit);

        // Act
        Result<WorkerResourceLimits> result = builder.Build();

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerResourceLimits>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(SettingsErrors.InvalidCpuLimitCode),
            () => error.Message.ShouldContain("0.5"),
            () => error.Message.ShouldContain("32"));
    }

    [Theory]
    [InlineData(63)]
    [InlineData(8193)]
    public void WhenPidsIsOutOfRange_FailsWithPidsCodeNamingMinAndMax(int pidsLimit)
    {
        // Arrange
        WorkerResourceLimitsBuilder builder = new WorkerResourceLimitsBuilder().WithPidsLimit(pidsLimit);

        // Act
        Result<WorkerResourceLimits> result = builder.Build();

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerResourceLimits>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(SettingsErrors.InvalidPidsLimitCode),
            () => error.Message.ShouldContain("64"),
            () => error.Message.ShouldContain("8192"));
    }

    [Theory]
    [InlineData(512, 0.5, 64)]
    [InlineData(65536, 32.0, 8192)]
    public void WhenValuesSitOnTheBounds_ReturnsSuccess(int memoryLimitMb, double cpuLimit, int pidsLimit)
    {
        // Arrange
        WorkerResourceLimitsBuilder builder = new WorkerResourceLimitsBuilder()
            .WithMemoryLimitMb(memoryLimitMb)
            .WithCpuLimit(cpuLimit)
            .WithPidsLimit(pidsLimit);

        // Act
        Result<WorkerResourceLimits> result = builder.Build();

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
