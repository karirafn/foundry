using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginSessionServiceTests;

/// <summary>
/// Regression tests for the background-task/SubmitCodeAsync concurrency race that caused
/// <see cref="ObjectDisposedException"/> on <c>_sessionTimeoutCts</c> in production.
/// </summary>
public sealed class ConcurrencyRace
{
    private const string OAuthUrl = "https://claude.ai/oauth/authorize?code=true";
    private const string UrlLine = $"If the browser didn't open, visit: {OAuthUrl}";

    private static LoginSessionService CreateService(
        FakeCredentialsOrchestrator orchestrator,
        FakeLoginSuccessCommitter? committer = null,
        ILoginSessionBroadcaster? broadcaster = null)
    {
        return new LoginSessionService(
            orchestrator,
            committer ?? new FakeLoginSuccessCommitter(),
            broadcaster ?? NullLoginSessionBroadcaster.Instance);
    }

    /// <summary>
    /// Regression test: SubmitCodeAsync must NOT throw ObjectDisposedException when the
    /// background task disposes _sessionTimeoutCts (via WaitForCodeOrTimeoutAsync returning)
    /// while SubmitCodeAsync is still blocked inside DeliverLoginCodeAsync.
    ///
    /// Race sequence reproduced deterministically:
    ///   1. Session starts, URL emitted — WaitingForAuthorization.
    ///   2. SubmitCodeAsync called → transitions to SigningIn → calls DeliverLoginCodeAsync (blocked by gate).
    ///   3. Background WaitForCodeOrTimeoutAsync polls, sees phase != WaitingForAuthorization, returns.
    ///   4. Background finally block: disposes _sessionTimeoutCts (background no longer owns session since phase=SigningIn).
    ///   5. Gate released — DeliverLoginCodeAsync completes.
    ///   6. SubmitCodeAsync continues without reading _sessionTimeoutCts → no ObjectDisposedException.
    ///   7. SubmitCodeAsync completes; session is cleared; IsLoginActive=false.
    /// </summary>
    [Fact]
    public async Task WhenBackgroundTaskDisposesCtsDuringDelivery_SubmitCodeAsyncSucceedsWithoutObjectDisposedException()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([UrlLine, "Login successful."]);
        orchestrator
            .WithExitedContainer(exitCode: 0)
            .WithBlockingDeliver();

        AccountIdentity expectedIdentity = new("alice@example.com", "Acme Corp", "pro");
        orchestrator.WithAuthStatusIdentity(expectedIdentity);

        FakeLoginSuccessCommitter committer = new();
        LoginSessionService sut = CreateService(orchestrator, committer);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Start SubmitCodeAsync — it will block inside DeliverLoginCodeAsync
        Task<Result> submitTask = sut.SubmitCodeAsync("abc123", TestContext.Current.CancellationToken);

        // Wait for the background task's 100ms poll to detect SigningIn and exit
        // WaitForCodeOrTimeoutAsync, then run through its finally block (disposes _sessionTimeoutCts).
        // 300ms is well above the 100ms tick interval.
        await Task.Delay(300, TestContext.Current.CancellationToken);

        // Release the blocked DeliverLoginCodeAsync — SubmitCodeAsync can now continue
        orchestrator.ReleaseDeliver();

        // Act — await SubmitCodeAsync; must NOT throw ObjectDisposedException
        Exception? thrown = null;
        try
        {
            await submitTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            thrown = ex;
        }

        // Assert — completed cleanly; session cleared after success
        thrown.ShouldBeNull("SubmitCodeAsync must not throw after background task disposes CTS");
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
        committer.CommitCallCount.ShouldBe(1);
        committer.CommittedIdentity.ShouldBe(expectedIdentity);
    }

    /// <summary>
    /// Sign-in-scan timeout (post-code, waiting for container confirmation) must produce
    /// Failed(CodeTimeout) and clear IsLoginActive — the sign-in CTS fires due to CancelAfter
    /// while the caller token (host) is still alive.
    /// </summary>
    [Fact]
    public async Task WhenSignInScanTimesOut_TransitionsToFailedWithCodeTimeout()
    {
        // Arrange — stream blocks after URL so WaitForLoginSuccessAsync blocks indefinitely
        FakeCredentialsOrchestrator orchestrator = new([UrlLine]);
        orchestrator.WithBlockingStreamAfterLines();

        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster: broadcaster);

        using CancellationTokenSource hostCts = new(TimeSpan.FromSeconds(30));
        await sut.StartAsync(hostCts.Token);
        await sut.WaitForStartCompletedAsync();

        // Start SubmitCodeAsync with the long-lived host token — the scan will block
        Task<Result> submitTask = sut.SubmitCodeAsync("any-code", hostCts.Token);

        // Give SubmitCodeAsync time to enter WaitForLoginSuccessAsync (after DeliverLoginCodeAsync)
        await Task.Delay(50, CancellationToken.None);

        // Act — trigger the sign-in scan CTS (simulates SignInTimeout expiring without host shutdown)
        sut.TriggerSignInTimeoutForTest();

        // Wait for SubmitCodeAsync to complete
        await submitTask;

        // Assert — session must be Failed(CodeTimeout), not hanging or unhandled exception
        LoginSessionUpdate? failedUpdate = broadcaster.Captured
            .FirstOrDefault(u => u.Phase == LoginPhaseDiscriminator.Failed);
        failedUpdate.ShouldNotBeNull("must reach Failed phase on sign-in scan timeout");
        failedUpdate!.FailureReason.ShouldBe(LoginFailureDiscriminator.CodeTimeout);
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }

    /// <summary>
    /// Host cancellation during sign-in scan must NOT broadcast a Failed phase.
    /// The session just disappears cleanly on shutdown.
    /// </summary>
    [Fact]
    public async Task WhenHostCancelledDuringSignInScan_DoesNotBroadcastFailed()
    {
        // Arrange — blocking stream after URL so sign-in log scan stays alive
        FakeCredentialsOrchestrator orchestrator = new([UrlLine]);
        orchestrator.WithBlockingStreamAfterLines();

        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster: broadcaster);

        using CancellationTokenSource hostCts = new();
        await sut.StartAsync(hostCts.Token);
        await sut.WaitForStartCompletedAsync();

        // Start code submission with the host token — it will block in the log scan
        Task<Result> submitTask = sut.SubmitCodeAsync("any-code", hostCts.Token);

        // Give the task time to enter the blocking scan
        await Task.Delay(50, CancellationToken.None);

        // Act — cancel host (app shutdown)
        await hostCts.CancelAsync();

        // Wait for submitTask — must complete (host shutdown exits cleanly)
        await submitTask;

        // Assert — no Failed broadcast during host shutdown
        bool anyFailed = broadcaster.Captured.Any(u => u.Phase == LoginPhaseDiscriminator.Failed);
        anyFailed.ShouldBeFalse("host shutdown must not broadcast Failed");
    }

    /// <summary>
    /// After a successful session, IsLoginActive is false and a second StartAsync can
    /// create a new session (no _activeSession leak).
    /// </summary>
    [Fact]
    public async Task WhenSessionSucceeds_IsLoginActiveFalse_AndNewSessionCanStart()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([UrlLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);

        AccountIdentity identity = new("user@example.com", "Org", "pro");
        orchestrator.WithAuthStatusIdentity(identity);

        FakeLoginSuccessCommitter committer = new();
        LoginSessionService sut = CreateService(orchestrator, committer);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();
        await sut.SubmitCodeAsync("code", TestContext.Current.CancellationToken);

        // Assert — session cleared
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();

        // A second StartAsync must succeed (not stuck behind a leaked session)
        Guid secondId = await sut.StartAsync(TestContext.Current.CancellationToken);
        secondId.ShouldNotBe(Guid.Empty);
    }
}
