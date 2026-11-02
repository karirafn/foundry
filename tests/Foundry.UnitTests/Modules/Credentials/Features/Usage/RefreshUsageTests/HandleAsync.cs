using Foundry.Modules.Credentials.Features.Usage;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.RefreshUsageTests;

public sealed class HandleAsync
{
    private sealed class StubUsagePollCoordinator(UsageRefreshOutcome outcome) : IUsagePollCoordinator
    {
        public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
            => Task.FromResult<UsageSnapshot?>(null);

        public Task<UsageRefreshOutcome> RequestRefreshAsync(CancellationToken cancellationToken)
            => Task.FromResult(outcome);
    }

    [Theory]
    [InlineData(nameof(UsageRefreshOutcome.Started), "started")]
    [InlineData(nameof(UsageRefreshOutcome.InFlight), "inFlight")]
    public async Task WhenPollStartedOrInFlight_Returns202WithoutSnapshot(string outcomeName, string expectedStatus)
    {
        // Arrange
        StubUsagePollCoordinator coordinator = new(Enum.Parse<UsageRefreshOutcome>(outcomeName));

        // Act
        IResult result = await RefreshUsage.Endpoint.HandleAsync(coordinator, CancellationToken.None);

        // Assert
        Accepted<RefreshUsage.Response> accepted = result.ShouldBeOfType<Accepted<RefreshUsage.Response>>();
        accepted.Value.ShouldNotBeNull();
        accepted.Value.Status.ShouldBe(expectedStatus);
    }

    [Fact]
    public async Task WhenAlreadyCurrent_Returns200WithCurrentStatus()
    {
        // Arrange
        StubUsagePollCoordinator coordinator = new(UsageRefreshOutcome.AlreadyCurrent);

        // Act
        IResult result = await RefreshUsage.Endpoint.HandleAsync(coordinator, CancellationToken.None);

        // Assert
        Ok<RefreshUsage.Response> ok = result.ShouldBeOfType<Ok<RefreshUsage.Response>>();
        ok.Value.ShouldNotBeNull();
        ok.Value.Status.ShouldBe("current");
    }

    [Theory]
    [InlineData(nameof(UsageRefreshOutcome.Unavailable), "unavailable")]
    [InlineData(nameof(UsageRefreshOutcome.LoginActive), "loginActive")]
    public async Task WhenRefused_Returns409WithReason(string outcomeName, string expectedStatus)
    {
        // Arrange
        StubUsagePollCoordinator coordinator = new(Enum.Parse<UsageRefreshOutcome>(outcomeName));

        // Act
        IResult result = await RefreshUsage.Endpoint.HandleAsync(coordinator, CancellationToken.None);

        // Assert
        Conflict<RefreshUsage.Response> conflict = result.ShouldBeOfType<Conflict<RefreshUsage.Response>>();
        conflict.Value.ShouldNotBeNull();
        conflict.Value.Status.ShouldBe(expectedStatus);
    }
}
