using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginSessionServiceTests;

public sealed class SubmitCodeAsync
{
    private static LoginSessionService CreateService(
        FakeWorkerOrchestrator orchestrator,
        FakeLoginSuccessCommitter? committer = null)
    {
        return new LoginSessionService(
            orchestrator,
            committer ?? new FakeLoginSuccessCommitter(),
            NullLoginSessionBroadcaster.Instance);
    }

    [Fact]
    public async Task WhenCodeSubmittedAndContainerExitsZero_TransitionsToSucceeded()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);

        AccountIdentity expectedIdentity = new("alice@example.com", "Acme Corp", "pro");
        orchestrator.WithAuthStatusIdentity(expectedIdentity);

        FakeLoginSuccessCommitter committer = new();
        LoginSessionService sut = CreateService(orchestrator, committer);
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SubmitCodeAsync("abc123", TestContext.Current.CancellationToken);

        // Assert
        session.Phase.ShouldBeOfType<LoginPhase.Succeeded>()
            .Identity.ShouldBe(expectedIdentity);
    }

    [Fact]
    public async Task WhenCodeSubmittedAndContainerExitsZero_CallsCommitter_WithCapturedIdentity()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);

        AccountIdentity expectedIdentity = new("bob@example.com", "Widgets Inc", "team");
        orchestrator.WithAuthStatusIdentity(expectedIdentity);

        FakeLoginSuccessCommitter committer = new();
        LoginSessionService sut = CreateService(orchestrator, committer);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SubmitCodeAsync("xyz789", TestContext.Current.CancellationToken);

        // Assert
        committer.CommitCallCount.ShouldBe(1);
        committer.CommittedIdentity.ShouldBe(expectedIdentity);
    }

    [Fact]
    public async Task WhenCodeSubmitted_DeliversCodeToOrchestrator()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);

        LoginSessionService sut = CreateService(orchestrator);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SubmitCodeAsync("the-code", TestContext.Current.CancellationToken);

        // Assert
        orchestrator.DeliverLoginCodeCallCount.ShouldBe(1);
        orchestrator.LastDeliveredCode.ShouldBe("the-code");
    }

    [Fact]
    public async Task WhenCodeSubmittedAndContainerExitsZero_TearsDownContainer()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);

        LoginSessionService sut = CreateService(orchestrator);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SubmitCodeAsync("abc123", TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBeGreaterThan(0);
        orchestrator.RemoveContainerCallCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WhenCodeSubmittedAndContainerExitsZero_IsLoginActiveBecomesFalse()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);

        LoginSessionService sut = CreateService(orchestrator);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SubmitCodeAsync("abc123", TestContext.Current.CancellationToken);

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenContainerExitsNonZero_TransitionsToFailed_WithInvalidCode()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        orchestrator.WithExitedContainer(exitCode: 1);

        LoginSessionService sut = CreateService(orchestrator);
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SubmitCodeAsync("bad-code", TestContext.Current.CancellationToken);

        // Assert
        LoginPhase.Failed failed = session.Phase.ShouldBeOfType<LoginPhase.Failed>();
        failed.Reason.ShouldBeOfType<LoginFailureReason.InvalidCode>();
    }

    [Fact]
    public async Task WhenContainerExitsNonZero_TearsDownContainer()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        orchestrator.WithExitedContainer(exitCode: 1);

        LoginSessionService sut = CreateService(orchestrator);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SubmitCodeAsync("bad-code", TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBeGreaterThan(0);
        orchestrator.RemoveContainerCallCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WhenContainerExitsNonZero_DoesNotCallCommitter()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        orchestrator.WithExitedContainer(exitCode: 1);

        FakeLoginSuccessCommitter committer = new();
        LoginSessionService sut = CreateService(orchestrator, committer);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SubmitCodeAsync("bad-code", TestContext.Current.CancellationToken);

        // Assert
        committer.CommitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNoActiveSession_SubmitCodeAsync_DoesNothing()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService sut = CreateService(orchestrator);

        // Act — no session started
        await sut.SubmitCodeAsync("any-code", TestContext.Current.CancellationToken);

        // Assert — no teardown, no crash
        orchestrator.DeliverLoginCodeCallCount.ShouldBe(0);
    }
}
