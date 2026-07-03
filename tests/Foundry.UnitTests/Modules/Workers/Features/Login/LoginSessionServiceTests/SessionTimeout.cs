using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Login;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginSessionServiceTests;

public sealed class SessionTimeout
{
    [Fact]
    public async Task WhenSessionTimesOutBeforeCodeSubmitted_IsLoginActiveBecomesFalse()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        await sut.StartAsync(cts.Token);
        await sut.WaitForStartCompletedAsync();

        // Act — trigger session timeout immediately (don't wait 10 minutes)
        sut.TriggerSessionTimeoutForTest();

        // Wait until the session becomes inactive (background task processes the cancellation)
        bool inactive = await WaitUntilAsync(
            () => !((ILoginSessionState)sut).IsLoginActive,
            TimeSpan.FromSeconds(5),
            cts.Token);

        // Assert
        inactive.ShouldBeTrue("Session should become inactive after CodeTimeout");
        sut.ActiveSessionPhaseForTest.ShouldBeNull();
    }

    [Fact]
    public async Task WhenSessionTimesOutBeforeCodeSubmitted_TearsDownContainer()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        await sut.StartAsync(cts.Token);
        await sut.WaitForStartCompletedAsync();

        // Act — trigger session timeout
        sut.TriggerSessionTimeoutForTest();

        await WaitUntilAsync(
            () => !((ILoginSessionState)sut).IsLoginActive,
            TimeSpan.FromSeconds(5),
            cts.Token);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBeGreaterThan(0);
        orchestrator.RemoveContainerCallCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WhenSessionTimesOutBeforeCodeSubmitted_BroadcastsFailedWithCodeTimeout()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), broadcaster);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        await sut.StartAsync(cts.Token);
        await sut.WaitForStartCompletedAsync();

        // Act — trigger session timeout
        sut.TriggerSessionTimeoutForTest();

        await WaitUntilAsync(
            () => !((ILoginSessionState)sut).IsLoginActive,
            TimeSpan.FromSeconds(5),
            cts.Token);

        // Assert
        LoginSessionUpdate? failedUpdate = broadcaster.Captured
            .FirstOrDefault(u => u.Phase == LoginPhaseDiscriminator.Failed);
        failedUpdate.ShouldNotBeNull();
        failedUpdate!.FailureReason.ShouldBe(LoginFailureDiscriminator.CodeTimeout);
    }

    /// <summary>
    /// Polls <paramref name="condition"/> every 50ms until it returns <c>true</c> or
    /// <paramref name="timeout"/> elapses. Returns whether the condition was satisfied.
    /// </summary>
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

    internal sealed class CapturingLoginSessionBroadcaster : ILoginSessionBroadcaster
    {
        private readonly List<LoginSessionUpdate> _captured = [];

        public IReadOnlyList<LoginSessionUpdate> Captured => _captured;

        public Task BroadcastAsync(LoginSessionUpdate update, CancellationToken cancellationToken)
        {
            _captured.Add(update);
            return Task.CompletedTask;
        }
    }
}
