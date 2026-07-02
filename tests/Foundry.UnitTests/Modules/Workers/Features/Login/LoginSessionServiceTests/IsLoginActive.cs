using Foundry.Modules.Workers.Features.Login;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginSessionServiceTests;

public sealed class IsLoginActive
{
    [Fact]
    public void WhenNoSessionStarted_ReturnsFalse()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService sut = new(orchestrator);

        // Act
        bool result = ((ILoginSessionState)sut).IsLoginActive;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSessionInWaitingForAuthorizationPhase_ReturnsTrue()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        FakeWorkerOrchestrator orchestrator = new([$"visit: {oauthUrl}"]);
        LoginSessionService sut = new(orchestrator);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = ((ILoginSessionState)sut).IsLoginActive;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenSessionTransitionedToFailed_ReturnsFalse()
    {
        // Arrange — no URL in log stream means session stays in Starting (not active after stream ends)
        // We test terminal-state clearing via direct Transition on the session
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService sut = new(orchestrator);
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act — simulate a terminal transition (e.g. timeout)
        session.Transition(new LoginPhase.Failed("Timed out waiting for URL"));

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSessionTransitionedToSucceeded_ReturnsFalse()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        FakeWorkerOrchestrator orchestrator = new([$"visit: {oauthUrl}"]);
        LoginSessionService sut = new(orchestrator);
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act — simulate successful login completion
        session.Transition(new LoginPhase.Succeeded(new AccountIdentity("user@example.com")));

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }
}
