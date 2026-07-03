using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Login;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginSessionServiceTests;

/// <summary>
/// Tests for finding #1: WaitForLoginSuccessAsync must not hang when the CLI emits
/// "Invalid code" and stays alive (re-prompting), and must produce Failed(InvalidCode).
/// Tests for finding #2: concurrent StartAsync calls must not create two sessions.
/// Tests for finding #3: host cancellation must clear _activeSession.
/// </summary>
public sealed class InvalidCodeHandling
{
    private const string OAuthUrl = "https://claude.ai/oauth/authorize?code=true";
    private const string UrlLine = $"If the browser didn't open, visit: {OAuthUrl}";

    private static LoginSessionService CreateService(
        FakeWorkerOrchestrator orchestrator,
        CapturingLoginSessionBroadcaster? broadcaster = null,
        FakeLoginSuccessCommitter? committer = null)
    {
        return new LoginSessionService(
            orchestrator,
            committer ?? new FakeLoginSuccessCommitter(),
            broadcaster ?? new CapturingLoginSessionBroadcaster());
    }

    /// <summary>
    /// Finding #1a: When the log stream emits "Invalid code" and then blocks indefinitely
    /// (CLI re-prompts without exiting), SubmitCodeAsync must NOT hang — it must detect
    /// the "Invalid code" line, terminate the stream scan, and transition to Failed(InvalidCode).
    /// </summary>
    [Fact]
    public async Task WhenCliEmitsInvalidCode_WithBlockingStream_SessionFailsWithInvalidCode()
    {
        // Arrange — stream yields URL + "Invalid code" then blocks indefinitely (no EOF)
        FakeWorkerOrchestrator orchestrator = new([UrlLine, "Invalid code. Please try again."]);
        orchestrator.WithBlockingStreamAfterLines();

        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act — submit code; stream blocks after "Invalid code"; must not hang
        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(10));
        await sut.SubmitCodeAsync("bogus-code", timeoutCts.Token);

        // Assert — session ended in Failed(InvalidCode), not a hang
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
        LoginSessionUpdate? failedUpdate = broadcaster.Captured
            .FirstOrDefault(u => u.Phase == LoginPhaseDiscriminator.Failed);
        failedUpdate.ShouldNotBeNull("session must reach Failed phase");
        failedUpdate!.FailureReason.ShouldBe(LoginFailureDiscriminator.InvalidCode);
    }

    /// <summary>
    /// Finding #1b: When the log stream emits "Invalid code" (case-insensitive), the session
    /// fails — the check must be case-insensitive to handle variations like "invalid code".
    /// </summary>
    [Fact]
    public async Task WhenCliEmitsInvalidCodeLowercase_SessionFailsWithInvalidCode()
    {
        // Arrange — lowercase variant of the rejection message
        FakeWorkerOrchestrator orchestrator = new([UrlLine, "invalid code, please try again"]);
        orchestrator.WithBlockingStreamAfterLines();

        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(10));
        await sut.SubmitCodeAsync("bogus-code", timeoutCts.Token);

        // Assert
        LoginSessionUpdate? failedUpdate = broadcaster.Captured
            .FirstOrDefault(u => u.Phase == LoginPhaseDiscriminator.Failed);
        failedUpdate.ShouldNotBeNull();
        failedUpdate!.FailureReason.ShouldBe(LoginFailureDiscriminator.InvalidCode);
    }

    /// <summary>
    /// Finding #2: Two concurrent StartAsync calls must create only one session and both
    /// return the same session ID (second call is idempotent — not a race).
    /// </summary>
    [Fact]
    public async Task WhenTwoConcurrentStartCalls_OnlyOneSessionCreated_BothReturnSameId()
    {
        // Arrange — orchestrator with URL so background task has stable work
        FakeWorkerOrchestrator orchestrator = new([UrlLine]);
        LoginSessionService sut = CreateService(orchestrator);

        // Act — fire two concurrent starts; the lock must ensure only one session is created
        Task<Guid> t1 = sut.StartAsync(TestContext.Current.CancellationToken);
        Task<Guid> t2 = sut.StartAsync(TestContext.Current.CancellationToken);

        Guid[] ids = await Task.WhenAll(t1, t2);

        // Assert — both returned the same session id (second call observed the first session)
        ids[0].ShouldBe(ids[1]);
        ids[0].ShouldNotBe(Guid.Empty);
    }

    /// <summary>
    /// Finding #3: When the host cancellation token fires, RunSessionAsync must clear
    /// _activeSession so IsLoginActive returns false (not stuck true forever).
    /// </summary>
    [Fact]
    public async Task WhenHostCancellationFires_IsLoginActiveBecomesFalse()
    {
        // Arrange — blocking stream so RunSessionAsync waits; host cancel must unblock it
        FakeWorkerOrchestrator orchestrator = new([UrlLine]);
        orchestrator.WithBlockingStreamAfterLines();
        LoginSessionService sut = CreateService(orchestrator);

        using CancellationTokenSource hostCts = new();
        await sut.StartAsync(hostCts.Token);
        await sut.WaitForStartCompletedAsync();

        // Confirm session is active before cancelling
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeTrue();

        // Act — cancel the host token
        await hostCts.CancelAsync();

        // Assert — session cleared within a reasonable window
        bool inactive = await WaitUntilAsync(
            () => !((ILoginSessionState)sut).IsLoginActive,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        inactive.ShouldBeTrue("_activeSession must be cleared when host token is cancelled");
    }

    private static async Task<bool> WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCts.CancelAfter(timeout);

        while (!deadlineCts.IsCancellationRequested)
        {
            if (condition())
            {
                return true;
            }

            try
            {
                await Task.Delay(50, deadlineCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return condition();
    }
}
