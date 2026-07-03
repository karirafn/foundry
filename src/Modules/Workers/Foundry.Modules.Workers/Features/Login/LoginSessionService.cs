using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Singleton service that manages a single in-memory OAuth login session.
/// Implements <see cref="ILoginSessionState"/> so dispatch can consult it without
/// a dependency on the full service.
/// </summary>
internal sealed class LoginSessionService(
    IWorkerOrchestrator orchestrator,
    ILoginSuccessCommitter successCommitter,
    ILoginSessionBroadcaster broadcaster,
    ILogger<LoginSessionService>? logger = null) : ILoginSessionState
{
    internal const string LoginSuccessSignal = "Login successful.";
    internal const string InvalidCodeSignal = "Invalid code";

    // Protects _activeSession null-check-and-assign so two concurrent StartAsync calls
    // cannot both observe null and create two sessions.
    private readonly Lock _sessionLock = new();

    private LoginSession? _activeSession;

    // Signals when the URL-scan phase is complete (URL found, UrlTimeout, or exception).
    // Exposed for tests so they can await the session reaching a stable phase before asserting.
    private TaskCompletionSource _urlScanComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // CTS that lets tests trigger CodeTimeout without waiting 10 minutes.
    // Also used to link the WaitForLoginSuccessAsync log-scan so it cannot outlive the session.
    private CancellationTokenSource? _sessionTimeoutCts;

    public bool IsLoginActive => _activeSession?.IsActive ?? false;

    /// <summary>
    /// Starts a new login session or returns the existing session id when one is already active (idempotent).
    /// Returns immediately — container start, URL scan, and timeouts run in a background task.
    /// The URL and result are delivered later via SignalR.
    /// </summary>
    internal Task<Guid> StartAsync(CancellationToken cancellationToken)
    {
        lock (_sessionLock)
        {
            if (_activeSession is not null)
            {
                return Task.FromResult(_activeSession.SessionId);
            }

            _urlScanComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            LoginSession session = LoginSession.Create(containerId: string.Empty);
            _activeSession = session;

            CancellationTokenSource sessionTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sessionTimeoutCts.CancelAfter(LoginSessionOptions.SessionTimeout);
            _sessionTimeoutCts = sessionTimeoutCts;

            // Fire-and-forget; errors are handled inside RunSessionAsync.
            // Pass CancellationToken.None to Task.Run so a precancelled host token does not
            // prevent the task from being scheduled — the linked sessionTimeoutCts already
            // carries the host signal.
            _ = Task.Run(
                () => RunSessionAsync(session, sessionTimeoutCts, cancellationToken),
                CancellationToken.None);

            return Task.FromResult(session.SessionId);
        }
    }

    /// <summary>
    /// Submits the operator's authorization code to the active login session.
    /// Returns a failure result if there is no session or the session is not in a state
    /// that accepts a code (i.e. not <see cref="LoginPhase.WaitingForAuthorization"/>).
    /// </summary>
    internal async Task<Result> SubmitCodeAsync(string code, CancellationToken cancellationToken)
    {
        LoginSession? session;
        lock (_sessionLock)
        {
            session = _activeSession;
        }

        if (session is null)
        {
            return Result.Fail(LoginErrors.NoActiveSession);
        }

        if (session.Phase is not LoginPhase.WaitingForAuthorization)
        {
            return Result.Fail(LoginErrors.NotAcceptingCode);
        }

        await TransitionAndBroadcastAsync(session, LoginPhase.SigningIn.Instance, cancellationToken);

        bool commitSucceeded = false;

        try
        {
            await orchestrator.DeliverLoginCodeAsync(session.ContainerId, code, cancellationToken);

            // Link the log scan to the session-timeout CTS so it cannot outlive the session.
            CancellationToken scanToken;
            lock (_sessionLock)
            {
                scanToken = _sessionTimeoutCts?.Token ?? cancellationToken;
            }

            using CancellationTokenSource linkedScan =
                CancellationTokenSource.CreateLinkedTokenSource(scanToken, cancellationToken);

            bool loginSuccessful = await WaitForLoginSuccessAsync(session.ContainerId, linkedScan.Token);

            if (!loginSuccessful)
            {
                await FailSessionAsync(session, new LoginFailureReason.InvalidCode(), cancellationToken);
                return Result.Ok();
            }

            // Capture identity BEFORE teardown — the container must still be up for auth status exec
            Result<AccountIdentity> identityResult =
                await orchestrator.GetAuthStatusAsync(session.ContainerId, cancellationToken);

            if (identityResult is not Result<AccountIdentity>.Success identitySuccess)
            {
                string? description = identityResult is Result<AccountIdentity>.Failure f
                    ? f.Error.Message
                    : null;

                logger?.LogWarning(
                    "Login container exited cleanly but auth status could not be read: {Error}",
                    description);

                await FailSessionAsync(session, new LoginFailureReason.Unknown(description), cancellationToken);
                return Result.Ok();
            }

            await successCommitter.CommitAsync(identitySuccess.Value, cancellationToken);
            commitSucceeded = true;

            await TransitionAndBroadcastAsync(
                session,
                new LoginPhase.Succeeded(identitySuccess.Value),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Unexpected error during login code submission.");

            // Only fail the session when the commit has NOT persisted — once committed, the
            // credential is durable and we must not flip observable state to Failed.
            if (!commitSucceeded)
            {
                await FailSessionAsync(session, new LoginFailureReason.Unknown(ex.Message), cancellationToken);
            }

            return Result.Ok();
        }

        await TeardownContainerAsync(session.ContainerId, cancellationToken);

        lock (_sessionLock)
        {
            _activeSession = null;
        }

        return Result.Ok();
    }

    /// <summary>
    /// Waits until the URL-scan phase has completed (URL found, UrlTimeout, or unexpected failure).
    /// After this task completes, the session is either in <see cref="LoginPhase.WaitingForAuthorization"/>
    /// or in a terminal <see cref="LoginPhase.Failed"/> state.
    /// Exposed for tests — the URL and result arrive via SignalR in production.
    /// </summary>
    internal Task WaitForStartCompletedAsync() => _urlScanComplete.Task;

    /// <summary>
    /// Cancels the session-timeout CTS immediately so the background task fires CodeTimeout
    /// without waiting the full 10 minutes. For tests only.
    /// </summary>
    internal void TriggerSessionTimeoutForTest() => _sessionTimeoutCts?.Cancel();

    /// <summary>
    /// The current session phase, or <c>null</c> when no session exists.
    /// Exposed for test observation only.
    /// </summary>
    internal LoginPhase? ActiveSessionPhaseForTest => _activeSession?.Phase;

    private async Task RunSessionAsync(
        LoginSession session,
        CancellationTokenSource sessionTimeoutCts,
        CancellationToken hostToken)
    {
        try
        {
            Result<ContainerId> startResult = await orchestrator.StartLoginContainerAsync(
                new LoginContainerSpec(TimeoutSeconds: (int)LoginSessionOptions.SessionTimeout.TotalSeconds),
                hostToken);

            string containerId = startResult is Result<ContainerId>.Success s
                ? s.Value.Value
                : string.Empty;

            // Patch the session's container ID now that we have it
            session.SetContainerId(containerId);

            await TransitionAndBroadcastAsync(session, LoginPhase.Starting.Instance, hostToken);

            // Scan for the OAuth URL with the URL-specific timeout
            await ScanForUrlAsync(session, sessionTimeoutCts.Token, hostToken);

            if (session.Phase is LoginPhase.Starting)
            {
                // Log stream exhausted without a URL — UrlTimeout
                await FailSessionAsync(
                    session,
                    new LoginFailureReason.UrlTimeout("No authorization URL received from the login container."),
                    hostToken);

                _urlScanComplete.TrySetResult();
                return;
            }

            // URL received — signal tests that startup phase is complete
            _urlScanComplete.TrySetResult();

            // Wait for code submission or session timeout
            await WaitForCodeOrTimeoutAsync(session, sessionTimeoutCts.Token, hostToken);
        }
        catch (OperationCanceledException) when (sessionTimeoutCts.IsCancellationRequested
                                                  && !hostToken.IsCancellationRequested)
        {
            // Session-level timeout fired (not host shutdown) — fail with CodeTimeout
            _urlScanComplete.TrySetResult();
            await FailSessionAsync(
                session,
                new LoginFailureReason.CodeTimeout("Session timed out waiting for authorization code."),
                hostToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Unexpected error in login session background task.");
            _urlScanComplete.TrySetException(ex);
            await FailSessionAsync(session, new LoginFailureReason.Unknown(ex.Message), CancellationToken.None);
        }
        finally
        {
            // EVERY exit path must clear _activeSession so IsLoginActive does not stay true.
            // This covers host cancellation, session timeout, URL timeout, and unexpected errors.
            _urlScanComplete.TrySetResult();

            lock (_sessionLock)
            {
                _activeSession = null;
            }

            sessionTimeoutCts.Dispose();
        }
    }

    /// <summary>
    /// Holds the background task in the URL-received state until the session leaves
    /// <see cref="LoginPhase.WaitingForAuthorization"/> (code submitted) or the session timeout fires.
    /// </summary>
    private static async Task WaitForCodeOrTimeoutAsync(
        LoginSession session,
        CancellationToken sessionTimeoutToken,
        CancellationToken hostToken)
    {
        // Poll at low cost until the phase changes (SubmitCodeAsync transitions out of WaitingForAuthorization)
        // or until the session-timeout CTS fires.
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));

        while (await timer.WaitForNextTickAsync(sessionTimeoutToken))
        {
            if (session.Phase is not LoginPhase.WaitingForAuthorization)
            {
                // Code was submitted — SubmitCodeAsync owns the rest
                return;
            }
        }
    }

    private async Task ScanForUrlAsync(
        LoginSession session,
        CancellationToken sessionTimeoutToken,
        CancellationToken hostToken)
    {
        using CancellationTokenSource urlTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            sessionTimeoutToken,
            hostToken);
        urlTimeoutCts.CancelAfter(LoginSessionOptions.UrlTimeout);

        try
        {
            await foreach (string line in orchestrator
                .StreamLogsAsync(session.ContainerId, urlTimeoutCts.Token)
                .ConfigureAwait(false))
            {
                string? url = AuthorizationUrlExtractor.Extract(line);

                if (url is not null)
                {
                    await TransitionAndBroadcastAsync(
                        session,
                        new LoginPhase.WaitingForAuthorization(url),
                        hostToken);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (urlTimeoutCts.IsCancellationRequested
                                                  && !hostToken.IsCancellationRequested)
        {
            // URL timeout fired — session stays in Starting; caller handles this
        }
    }

    private async Task TransitionAndBroadcastAsync(
        LoginSession session,
        LoginPhase next,
        CancellationToken cancellationToken)
    {
        session.Transition(next);

        LoginSessionUpdate update = BuildUpdate(session.SessionId, next);

        try
        {
            await broadcaster.BroadcastAsync(update, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Failed to broadcast login session update for phase {Phase}.", next.GetType().Name);
        }
    }

    private static LoginSessionUpdate BuildUpdate(Guid sessionId, LoginPhase phase)
    {
        string phaseDiscriminator = phase switch
        {
            LoginPhase.Starting => LoginPhaseDiscriminator.Starting,
            LoginPhase.WaitingForAuthorization => LoginPhaseDiscriminator.WaitingForAuthorization,
            LoginPhase.SigningIn => LoginPhaseDiscriminator.SigningIn,
            LoginPhase.Succeeded => LoginPhaseDiscriminator.Succeeded,
            LoginPhase.Failed => LoginPhaseDiscriminator.Failed,
            _ => LoginPhaseDiscriminator.Failed,
        };

        string? authorizationUrl = phase is LoginPhase.WaitingForAuthorization waiting
            ? waiting.Url
            : null;

        (string? failureReason, string? failureMessage) = phase is LoginPhase.Failed failed
            ? MapFailureReason(failed.Reason)
            : (null, null);

        return new LoginSessionUpdate(sessionId, phaseDiscriminator, authorizationUrl, failureReason, failureMessage);
    }

    private static (string Reason, string? Message) MapFailureReason(LoginFailureReason reason) =>
        reason switch
        {
            LoginFailureReason.InvalidCode r => (LoginFailureDiscriminator.InvalidCode, r.Message),
            LoginFailureReason.UrlTimeout r => (LoginFailureDiscriminator.UrlTimeout, r.Message),
            LoginFailureReason.CodeTimeout r => (LoginFailureDiscriminator.CodeTimeout, r.Message),
            LoginFailureReason.Unknown r => (LoginFailureDiscriminator.Unknown, r.Message),
            _ => (LoginFailureDiscriminator.Unknown, null),
        };

    /// <summary>
    /// Waits for the login success signal or "Invalid code" rejection in the log stream.
    /// Returns <c>true</c> when login succeeded (success signal seen AND container exit 0).
    /// Returns <c>false</c> when an "Invalid code" rejection is detected (CLI re-prompts;
    /// the stream is cancelled via <paramref name="cancellationToken"/> which is linked to
    /// the session-timeout CTS so the scan can never outlive the session).
    /// </summary>
    private async Task<bool> WaitForLoginSuccessAsync(string containerId, CancellationToken cancellationToken)
    {
        bool successSeen = false;

        await foreach (string line in orchestrator
            .StreamLogsAsync(containerId, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            if (line.Contains(InvalidCodeSignal, StringComparison.OrdinalIgnoreCase))
            {
                // CLI rejected the code and will re-prompt — stream stays open indefinitely.
                // Treat this as definitive failure immediately rather than waiting for EOF.
                return false;
            }

            if (line.Contains(LoginSuccessSignal, StringComparison.Ordinal))
            {
                successSeen = true;
                break;
            }
        }

        if (!successSeen)
        {
            return false;
        }

        WorkerStatus? status = await orchestrator.GetStatusAsync(containerId, cancellationToken);

        // Treat unknown status conservatively as failure
        return status?.ExitCode is 0;
    }

    private async Task FailSessionAsync(
        LoginSession session,
        LoginFailureReason reason,
        CancellationToken cancellationToken)
    {
        await TransitionAndBroadcastAsync(session, new LoginPhase.Failed(reason), cancellationToken);
        await TeardownContainerAsync(session.ContainerId, cancellationToken);

        lock (_sessionLock)
        {
            _activeSession = null;
        }
    }

    private async Task TeardownContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(containerId))
        {
            return;
        }

        try
        {
            await orchestrator.StopContainerAsync(containerId, cancellationToken);
            await orchestrator.RemoveContainerAsync(containerId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Failed to teardown login container {ContainerId}.", containerId);
        }
    }
}
