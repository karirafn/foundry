using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageToolchainRecorderTests;

public sealed class ProductionRetryPolicy
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(20, 30)]
    public void StartsAtOneMinuteDoublingCappedAtThirty(int attempt, int expectedMinutes)
    {
        WorkerImageToolchainRecorder.ProductionRetryPolicy.ComputeBackoff(attempt)
            .ShouldBe(TimeSpan.FromMinutes(expectedMinutes));
    }
}
