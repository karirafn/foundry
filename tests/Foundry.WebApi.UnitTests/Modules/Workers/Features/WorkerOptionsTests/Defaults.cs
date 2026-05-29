using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.WorkerOptionsTests;

public sealed class Defaults
{
    [Fact]
    public void WhenDefaultOptionsCreated_HasExpectedDefaults()
    {
        // Arrange / Act
        WorkerOptions options = new();

        // Assert
        options.ShouldSatisfyAllConditions(
            () => options.Image.ShouldBe("ghcr.io/anthropics/claude-code:latest"),
            () => options.MaxConcurrent.ShouldBe(3),
            () => options.TimeoutMinutes.ShouldBe(120),
            () => options.ConfigPath.ShouldBe("./workers/config"),
            () => options.BranchNamingInstruction.ShouldBe("Use conventional branch naming"),
            () => options.ReportsPath.ShouldBe("./data/reports"),
            () => options.SystemPromptTemplate.ShouldNotBeNullOrWhiteSpace(),
            () => options.MemoryLimitMb.ShouldBe(8192),
            () => options.CpuLimit.ShouldBe(2.0),
            () => options.PidsLimit.ShouldBe(512));
    }
}
