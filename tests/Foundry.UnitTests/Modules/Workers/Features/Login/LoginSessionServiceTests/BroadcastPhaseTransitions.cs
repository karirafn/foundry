using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginSessionServiceTests;

public sealed class BroadcastPhaseTransitions
{
    private static LoginSessionService CreateService(
        FakeCredentialsOrchestrator orchestrator,
        CapturingLoginSessionBroadcaster broadcaster,
        FakeLoginSuccessCommitter? committer = null)
    {
        return new LoginSessionService(
            orchestrator,
            committer ?? new FakeLoginSuccessCommitter(),
            broadcaster);
    }

    [Fact]
    public async Task WhenStartAsyncCalled_BroadcastsStartingPhase()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Assert
        broadcaster.Captured.ShouldContain(u => u.Phase == LoginPhaseDiscriminator.Starting);
    }

    [Fact]
    public async Task WhenUrlReceived_BroadcastsWaitingForAuthorizationPhase()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Assert
        broadcaster.Captured.ShouldContain(u => u.Phase == LoginPhaseDiscriminator.WaitingForAuthorization);
        LoginSessionUpdate update = broadcaster.Captured
            .Single(u => u.Phase == LoginPhaseDiscriminator.WaitingForAuthorization);
        update.AuthorizationUrl.ShouldBe(url);
    }

    [Fact]
    public async Task WhenNoUrlReceived_BroadcastsFailedPhase()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([]);
        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Assert
        broadcaster.Captured.ShouldContain(u => u.Phase == LoginPhaseDiscriminator.Failed);
    }

    [Fact]
    public async Task WhenCodeSubmitted_BroadcastsSigningInPhase()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeCredentialsOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);
        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        await sut.SubmitCodeAsync("abc123", TestContext.Current.CancellationToken);

        // Assert
        broadcaster.Captured.ShouldContain(u => u.Phase == LoginPhaseDiscriminator.SigningIn);
    }

    [Fact]
    public async Task WhenLoginSucceeds_BroadcastsSucceededPhase()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeCredentialsOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);
        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        await sut.SubmitCodeAsync("abc123", TestContext.Current.CancellationToken);

        // Assert
        broadcaster.Captured.ShouldContain(u => u.Phase == LoginPhaseDiscriminator.Succeeded);
    }

    [Fact]
    public async Task WhenLoginFails_BroadcastsFailedPhaseWithReason()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        orchestrator.WithExitedContainer(exitCode: 1);
        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        await sut.SubmitCodeAsync("bad-code", TestContext.Current.CancellationToken);

        // Assert
        broadcaster.Captured.ShouldContain(u => u.Phase == LoginPhaseDiscriminator.Failed);
        LoginSessionUpdate failed = broadcaster.Captured
            .Last(u => u.Phase == LoginPhaseDiscriminator.Failed);
        failed.FailureReason.ShouldBe(LoginFailureDiscriminator.InvalidCode);
    }

}
