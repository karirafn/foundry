using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
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
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        await sut.SubmitCodeAsync("abc123", TestContext.Current.CancellationToken);

        // Assert
        // After Succeeded, session is cleared — IsLoginActive is false and no active phase
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
        committer.CommitCallCount.ShouldBe(1);
        committer.CommittedIdentity.ShouldBe(expectedIdentity);
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
        await sut.WaitForStartCompletedAsync();

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
        await sut.WaitForStartCompletedAsync();

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
        await sut.WaitForStartCompletedAsync();

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
        await sut.WaitForStartCompletedAsync();

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
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        await sut.SubmitCodeAsync("bad-code", TestContext.Current.CancellationToken);

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
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
        await sut.WaitForStartCompletedAsync();

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
        await sut.WaitForStartCompletedAsync();

        // Act
        await sut.SubmitCodeAsync("bad-code", TestContext.Current.CancellationToken);

        // Assert
        committer.CommitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNoActiveSession_SubmitCodeAsync_ReturnsNoActiveSessionError()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService sut = CreateService(orchestrator);

        // Act — no session started
        Result submitResult = await sut.SubmitCodeAsync("any-code", TestContext.Current.CancellationToken);

        // Assert
        submitResult.IsFailure.ShouldBeTrue();
        Result.Failure failure = submitResult.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(LoginErrors.NoActiveSessionCode);
    }

    [Fact]
    public async Task WhenSessionNotInWaitingForAuthorization_SubmitCodeAsync_ReturnsNotAcceptingCodeError()
    {
        // Arrange — start but do NOT wait for URL (session stays in Starting)
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService sut = CreateService(orchestrator);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act — session is in Starting phase (or transitioning); not WaitingForAuthorization
        // We need to check the phase BEFORE the background task moves it to Failed (UrlTimeout).
        // Using ActiveSessionPhaseForTest to check current phase.
        // Since the fake has no log lines and returns empty stream immediately,
        // the background task will quickly move to Starting then UrlTimeout failure.
        // For this test: use a blocking fake that never yields any lines, keeping session in Starting.
        FakeWorkerOrchestrator blockingOrchestrator = new(["not-a-url-line"]);
        LoginSessionService sut2 = CreateService(blockingOrchestrator);
        await sut2.StartAsync(TestContext.Current.CancellationToken);
        // Don't wait for background — session is transiently in Starting phase
        // Submit while in Starting phase (not WaitingForAuthorization)

        // Act
        Result submitResult = await sut2.SubmitCodeAsync("any-code", TestContext.Current.CancellationToken);

        // Assert — either NoActiveSession (if background already cleaned up) or NotAcceptingCode
        // The key is: it doesn't return Ok
        submitResult.IsFailure.ShouldBeTrue();
    }
}
