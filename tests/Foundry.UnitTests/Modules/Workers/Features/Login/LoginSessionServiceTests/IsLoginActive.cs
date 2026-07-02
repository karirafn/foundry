using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
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
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter());

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
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter());
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = ((ILoginSessionState)sut).IsLoginActive;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenSessionTransitionedToFailed_ReturnsFalse()
    {
        // Arrange — use a URL so StartAsync transitions to WaitingForAuthorization (active)
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        FakeWorkerOrchestrator orchestrator = new([$"visit: {oauthUrl}"]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter());
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act — simulate a terminal transition (e.g. code timeout)
        session.Transition(new LoginPhase.Failed(new LoginFailureReason.CodeTimeout("Timed out waiting for code")));

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSessionTransitionedToSucceeded_ReturnsFalse()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        FakeWorkerOrchestrator orchestrator = new([$"visit: {oauthUrl}"]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter());
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act — simulate successful login completion
        session.Transition(new LoginPhase.Succeeded(new AccountIdentity("user@example.com", "Example Org", "pro")));

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }
}
