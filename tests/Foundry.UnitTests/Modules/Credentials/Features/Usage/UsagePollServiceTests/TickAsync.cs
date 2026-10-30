using Foundry.Modules.Credentials.Features.Usage;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsagePollServiceTests;

public sealed class TickAsync
{
    // ---------------------------------------------------------------------------
    // Stub
    // ---------------------------------------------------------------------------

    private sealed class StubUsagePollCoordinator : IUsagePollCoordinator
    {
        public int CallCount { get; private set; }
        public UsageSnapshot? ResultSnapshot { get; set; }

        public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(ResultSnapshot);
        }
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenTickFires_CallsCoordinator()
    {
        // Arrange
        StubUsagePollCoordinator coordinator = new();
        UsagePollService sut = new(coordinator, NullLogger<UsagePollService>.Instance);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTickedTwice_CallsCoordinatorTwice()
    {
        // Arrange
        StubUsagePollCoordinator coordinator = new();
        UsagePollService sut = new(coordinator, NullLogger<UsagePollService>.Instance);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(2);
    }

    [Fact]
    public void TickInterval_IsFiveMinutes()
    {
        // Arrange
        StubUsagePollCoordinator coordinator = new();
        UsagePollService sut = new(coordinator, NullLogger<UsagePollService>.Instance);

        // Act
        TimeSpan interval = UsagePollService.DefaultTickInterval;

        // Assert
        interval.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void RunTickOnStartup_IsTrue()
    {
        // Arrange
        StubUsagePollCoordinator coordinator = new();
        UsagePollService sut = new(coordinator, NullLogger<UsagePollService>.Instance);

        // Act
        bool result = sut.RunTickOnStartupForTest;

        // Assert
        result.ShouldBeTrue("UsagePollService must poll at startup per AC: 'plus once at startup'");
    }
}
