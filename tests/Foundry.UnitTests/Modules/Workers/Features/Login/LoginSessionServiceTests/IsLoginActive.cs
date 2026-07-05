using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure;
using Foundry.UnitTests.Fakes.Credentials;
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
        FakeCredentialsOrchestrator orchestrator = new([]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

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
        FakeCredentialsOrchestrator orchestrator = new([$"visit: {oauthUrl}"]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        bool result = ((ILoginSessionState)sut).IsLoginActive;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenSessionTransitionedToFailed_ReturnsFalse()
    {
        // Arrange — session transitions to WaitingForAuthorization then we trigger timeout
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        FakeCredentialsOrchestrator orchestrator = new([$"visit: {oauthUrl}"]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act — trigger session timeout to get to Failed state
        sut.TriggerSessionTimeoutForTest();
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSessionTransitionedToSucceeded_ReturnsFalse()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";
        FakeCredentialsOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);
        AccountIdentity identity = new("user@example.com", "Example Org", "pro");
        orchestrator.WithAuthStatusIdentity(identity);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act — submit code to complete and reach Succeeded
        await sut.SubmitCodeAsync("any-code", TestContext.Current.CancellationToken);

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }
}
