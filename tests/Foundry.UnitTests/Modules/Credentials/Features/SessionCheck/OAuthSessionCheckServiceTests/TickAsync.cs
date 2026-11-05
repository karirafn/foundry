using Foundry.Modules.Credentials.Features.SessionCheck;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.SessionCheck.OAuthSessionCheckServiceTests;

public sealed class TickAsync
{
    private sealed class StubCoordinator : IOAuthSessionCheckCoordinator
    {
        public int CallCount { get; private set; }

        public Task<OAuthSessionStatus?> CheckAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<OAuthSessionStatus?>(new OAuthSessionStatus.Active());
        }
    }

    [Fact]
    public async Task WhenTickFires_RunsOneSessionCheck()
    {
        // Arrange
        StubCoordinator coordinator = new();
        OAuthSessionCheckService sut = new(coordinator, NullLogger<OAuthSessionCheckService>.Instance);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(1);
    }

    [Fact]
    public void RunsOnStartupAndEveryFiveMinutes()
    {
        // Arrange
        OAuthSessionCheckService sut = new(new StubCoordinator(), NullLogger<OAuthSessionCheckService>.Instance);

        // Assert
        sut.RunTickOnStartupForTest.ShouldBeTrue();
        OAuthSessionCheckService.DefaultTickInterval.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
