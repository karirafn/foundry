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

    /// <summary>
    /// Regression test for the bug where SubmitCodeAsync called GetAuthStatusAsync on the
    /// now-stopped login container (Docker throws "container is not running" on exec against a
    /// stopped container). Identity must be read from the credential volume via
    /// GetCredentialVolumeAuthStatusAsync, which is lifecycle-independent.
    /// </summary>
    [Fact]
    public async Task WhenLoginSucceeds_IdentityReadFromCredentialVolume_NotFromStoppedLoginContainer()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        // Login container exits with code 0 — it is stopped when GetAuthStatusAsync is called on it.
        // The faithful fake throws on exec-against-stopped-container, so if the production code
        // still calls GetAuthStatusAsync(containerId, ...) the test would fail with Unknown failure.
        FakeWorkerOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);

        AccountIdentity expectedIdentity = new("carol@example.com", "Acme Ltd", "pro");
        orchestrator.WithAuthStatusIdentity(expectedIdentity);

        FakeLoginSuccessCommitter committer = new();
        LoginSessionService sut = CreateService(orchestrator, committer);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        await sut.SubmitCodeAsync("valid-code", TestContext.Current.CancellationToken);

        // Assert — volume method was called (not exec on the stopped login container)
        orchestrator.GetCredentialVolumeAuthStatusCallCount.ShouldBe(1);
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
    public async Task WhenSessionInSigningInPhase_SubmitCodeAsync_ReturnsNotAcceptingCodeError()
    {
        // Arrange — reach WaitingForAuthorization, then move to SigningIn by submitting a code.
        // A second submit while in SigningIn must deterministically return NotAcceptingCode.
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        // Use a blocking-stream orchestrator so the log scan for the second code delivery
        // does not race to completion — the session stays in SigningIn throughout.
        FakeWorkerOrchestrator orchestrator = new([logLine]);
        orchestrator.WithBlockingStreamAfterLines();
        LoginSessionService sut = CreateService(orchestrator);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Deliver first code — transitions to SigningIn; the WaitForLoginSuccessAsync
        // log scan will block indefinitely (no "Login successful." or "Invalid code").
        // We do NOT await SubmitCodeAsync to keep the session in SigningIn.
        Task<Result> firstSubmit = sut.SubmitCodeAsync("first-code", TestContext.Current.CancellationToken);

        // Wait a brief moment for the state machine to advance past WaitingForAuthorization
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act — second submit while in SigningIn
        Result submitResult = await sut.SubmitCodeAsync("second-code", TestContext.Current.CancellationToken);

        // Assert — second submit is rejected with NotAcceptingCode
        submitResult.IsFailure.ShouldBeTrue();
        Result.Failure failure = submitResult.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(LoginErrors.NotAcceptingCodeCode);

        // Cleanup — cancel the first submit (it's blocked waiting for the log stream).
        // TriggerSignInTimeoutForTest cancels the sign-in scan CTS, which propagates
        // cancellation into the blocking WaitForLoginSuccessAsync call.
        // The resulting CodeTimeout failure is handled cleanly inside SubmitCodeAsync.
        sut.TriggerSignInTimeoutForTest();
        await firstSubmit;
    }
}
