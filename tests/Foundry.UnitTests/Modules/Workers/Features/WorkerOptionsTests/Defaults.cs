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
            () => options.Image.ShouldBe("ghcr.io/anthropics/claude-code"),
            () => options.MaxConcurrent.ShouldBe(3),
            () => options.TimeoutMinutes.ShouldBe(120),
            () => options.Mounts.ShouldBeEmpty(),
            () => options.WritableMounts.ShouldBeEmpty(),
            () => options.BranchNamingInstruction.ShouldBe("Use conventional branch naming"),
            () => options.ReportsPath.ShouldBe("./data/reports"),
            () => options.SystemPromptTemplate.ShouldNotBeNullOrWhiteSpace(),
            () => options.WorkerPromptTemplate.ShouldNotBeNullOrWhiteSpace(),
            () => options.WorkerPromptTemplate.ShouldContain("{issueNumber}"),
            () => options.MemoryLimitMb.ShouldBe(8192),
            () => options.CpuLimit.ShouldBe(2.0),
            () => options.PidsLimit.ShouldBe(512),
            () => options.Settings.ShouldNotBeNull(),
            () => options.Settings.AdditionalDenyRules.ShouldBeEmpty(),
            () => options.Settings.Model.ShouldBeNull());
    }
}
