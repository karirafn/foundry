using Foundry.Modules.Credentials.Features.CreditProbe;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.CreditProbe.CheckCreditsNowTests;

public sealed class HandleAsync
{
    private sealed class StubCreditProbeCoordinator(CreditProbeResult result) : ICreditProbeCoordinator
    {
        public bool WasCalled { get; private set; }

        public Task<CreditProbeResult> TryRunProbeAsync(CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task WhenRestored_Returns200WithResult()
    {
        // Arrange
        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.Restored());

        // Act
        IResult result = await CheckCreditsNow.Endpoint.HandleAsync(coordinator, CancellationToken.None);

        // Assert
        Ok<CheckCreditsNow.Response> ok = result.ShouldBeOfType<Ok<CheckCreditsNow.Response>>();
        ok.Value.ShouldNotBeNull();
        ok.Value.InFlight.ShouldBeFalse();
        ok.Value.Outcome.ShouldBe("restored");
    }

    [Fact]
    public async Task WhenAlreadyRunning_Returns202()
    {
        // Arrange
        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.AlreadyRunning());

        // Act
        IResult result = await CheckCreditsNow.Endpoint.HandleAsync(coordinator, CancellationToken.None);

        // Assert
        Accepted<CheckCreditsNow.Response> accepted = result.ShouldBeOfType<Accepted<CheckCreditsNow.Response>>();
        accepted.Value.ShouldNotBeNull();
        accepted.Value.InFlight.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenStillBlocked_Returns200WithResult()
    {
        // Arrange
        DateTimeOffset nextProbeAt = DateTimeOffset.UtcNow.AddMinutes(10);
        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.StillBlocked(nextProbeAt));

        // Act
        IResult result = await CheckCreditsNow.Endpoint.HandleAsync(coordinator, CancellationToken.None);

        // Assert
        Ok<CheckCreditsNow.Response> ok = result.ShouldBeOfType<Ok<CheckCreditsNow.Response>>();
        ok.Value.ShouldNotBeNull();
        ok.Value.InFlight.ShouldBeFalse();
        ok.Value.Outcome.ShouldBe("stillBlocked");
    }

    [Fact]
    public async Task WhenNotBlocked_Returns200WithResult()
    {
        // Arrange
        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.NotBlocked());

        // Act
        IResult result = await CheckCreditsNow.Endpoint.HandleAsync(coordinator, CancellationToken.None);

        // Assert
        Ok<CheckCreditsNow.Response> ok = result.ShouldBeOfType<Ok<CheckCreditsNow.Response>>();
        ok.Value.ShouldNotBeNull();
        ok.Value.Outcome.ShouldBe("notBlocked");
    }

    [Fact]
    public async Task WhenCalled_InvokesCoordinator()
    {
        // Arrange
        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.NotBlocked());

        // Act
        await CheckCreditsNow.Endpoint.HandleAsync(coordinator, CancellationToken.None);

        // Assert
        coordinator.WasCalled.ShouldBeTrue();
    }
}
