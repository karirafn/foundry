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

    /// <summary>
    /// Fixed opaque failure message broadcast to clients for Unknown failures.
    /// The real cause is logged server-side and never sent over the wire.
    /// </summary>
    internal const string UnknownFailureClientMessage = "An unexpected error occurred.";

    // Protects _activeSession null-check-and-assign so two concurrent StartAsync calls
    // cannot both observe null and create two sessions.
    private readonly Lock _sessionLock = new();

    private LoginSession? _activeSession;

    // Signals when the URL-scan phase is complete (URL found, UrlTimeout, or exception).
    // Exposed for tests so they can await the session reaching a stable phase before asserting.
    private TaskCompletionSource _urlScanComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // CTS that lets tests trigger CodeTimeout without waiting 10 minutes.
    // Owned exclusively by the background task RunSessionAsync — SubmitCodeAsync must NOT read it.
    private CancellationTokenSource? _sessionTimeoutCts;

    // Self-owned CTS for the sign-in log scan. Set when SubmitCodeAsync begins the scan,
    // cleared when done. Exposed for tests so they can trigger a sign-in timeout without
    // waiting the full 2 minutes.
    private CancellationTokenSource? _signInTimeoutCts;

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
                async () =>
                {
                    try
                    {
                        await RunSessionAsync(session, sessionTimeoutCts, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Prevent the fire-and-forget task from faulting unobserved.
                        // RunSessionAsync handles all expected exceptions internally; any
                        // escape here is a programming error and must be logged.
                        logger?.LogError(ex, "Unobserved exception escaped RunSessionAsync.");
                    }
                },
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

        // Self-owned sign-in scan CTS — decoupled from _sessionTimeoutCts so the background
        // task can dispose _sessionTimeoutCts without racing with this method.
        // NOT linked to cancellationToken here; linked below via linkedSignIn so we can
        // distinguish "our timer fired" (CodeTimeout) from "host shutdown" (no broadcast).
        using CancellationTokenSource signInCts = new();
        signInCts.CancelAfter(LoginSessionOptions.SignInTimeout);
        _signInTimeoutCts = signInCts;

        // Link the scan token so host shutdown propagates into WaitForLoginSuccessAsync.
        using CancellationTokenSource linkedSignIn =
            CancellationTokenSource.CreateLinkedTokenSource(signInCts.Token, cancellationToken);

        bool commitSucceeded = false;

        try
        {
            await orchestrator.DeliverLoginCodeAsync(session.ContainerId, code, cancellationToken);

            bool loginSuccessful = await WaitForLoginSuccessAsync(session.ContainerId, linkedSignIn.Token);

            if (!loginSuccessful)
            {
                await FailSessionAsync(session, new LoginFailureReason.InvalidCode(), cancellationToken);
                return Result.Ok();
            }

            // Read identity from the credential volume via a fresh helper container.
            // The login container has already exited by the time WaitForLoginSuccessAsync returns —
            // the CLI writes credentials to the volume before exiting, so we mount the same volume
            // in a short-lived helper and exec auth status there rather than on the stopped login container.
            Result<AccountIdentity> identityResult =
                await orchestrator.GetCredentialVolumeAuthStatusAsync(cancellationToken);

            if (identityResult is not Result<AccountIdentity>.Success identitySuccess)
            {
                string? description = identityResult is Result<AccountIdentity>.Failure f
                    ? f.Error.Message
                    : null;

                logger?.LogWarning(
                    "Login container exited cleanly but auth status could not be read: {Error}",
                    description);

                // Log the real detail server-side; broadcast a fixed opaque message to clients.
                await FailSessionAsync(
                    session,
                    new LoginFailureReason.Unknown(UnknownFailureClientMessage),
                    cancellationToken);
                return Result.Ok();
            }

            await successCommitter.CommitAsync(identitySuccess.Value, cancellationToken);
            commitSucceeded = true;

            await TransitionAndBroadcastAsync(
                session,
                new LoginPhase.Succeeded(identitySuccess.Value),
                cancellationToken);
        }
        catch (OperationCanceledException) when (signInCts.IsCancellationRequested
                                                  && !cancellationToken.IsCancellationRequested)
        {
            // Sign-in-scan timeout fired (not host shutdown) — treat as CodeTimeout.
            _signInTimeoutCts = null;
            await FailSessionAsync(
                session,
                new LoginFailureReason.CodeTimeout("Sign-in timed out waiting for login confirmation."),
                CancellationToken.None);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown — do NOT broadcast Failed; just clear the session and exit.
            _signInTimeoutCts = null;
            ClearActiveSession(session);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log the real cause server-side; broadcast an opaque fixed message to clients.
            logger?.LogError(ex, "Unexpected error during login code submission.");
            _signInTimeoutCts = null;

            // Only fail the session when the commit has NOT persisted — once committed, the
            // credential is durable and we must not flip observable state to Failed.
            if (!commitSucceeded)
            {
                await FailSessionAsync(
                    session,
                    new LoginFailureReason.Unknown(UnknownFailureClientMessage),
                    CancellationToken.None);
            }
            else
            {
                // Committed but post-commit work threw — clear the session so IsLoginActive
                // does not stay true forever (the commit is durable; only in-memory state leaks).
                ClearActiveSession(session);
            }

            return Result.Ok();
        }

        _signInTimeoutCts = null;
        await TeardownContainerAsync(session.ContainerId, cancellationToken);

        ClearActiveSession(session);

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
    /// Cancels the sign-in scan CTS immediately so SubmitCodeAsync fires CodeTimeout
    /// without waiting the full 2 minutes. For tests only.
    /// </summary>
    internal void TriggerSignInTimeoutForTest() => _signInTimeoutCts?.Cancel();

    // Maximum length of a single log line accepted for URL extraction and broadcast.
    // Lines longer than this are skipped — a legitimate OAuth URL is never this long.
    private const int MaxUrlLineLengthChars = 8_192;

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
        catch (OperationCanceledException) when (hostToken.IsCancellationRequested)
        {
            // Host shutdown — exit cleanly without broadcasting Failed.
            // _activeSession is cleared in the finally block only when we still own it.
            _urlScanComplete.TrySetResult();
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
            // Log the real cause server-side; broadcast an opaque fixed message to clients.
            logger?.LogError(ex, "Unexpected error in login session background task.");
            _urlScanComplete.TrySetException(ex);
            await FailSessionAsync(
                session,
                new LoginFailureReason.Unknown(UnknownFailureClientMessage),
                CancellationToken.None);
        }
        finally
        {
            _urlScanComplete.TrySetResult();

            // The background task only clears _activeSession when it still owns the session
            // (i.e. the phase is Starting or WaitingForAuthorization — before handoff).
            // After SubmitCodeAsync transitions to SigningIn, ownership transfers and the
            // background must NOT clear — SubmitCodeAsync is responsible from that point.
            // FailSessionAsync already clears on its own terminal paths.
            if (session.Phase is LoginPhase.Starting or LoginPhase.WaitingForAuthorization)
            {
                ClearActiveSession(session);
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
                // Skip oversized lines — a legitimate OAuth URL is never 8 KB long.
                // This prevents a misbehaving container from injecting a huge string into
                // the WaitingForAuthorization phase and over the SignalR broadcast.
                if (line.Length > MaxUrlLineLengthChars)
                {
                    continue;
                }

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
    /// the stream is cancelled via <paramref name="cancellationToken"/> which is the self-owned
    /// sign-in CTS so the scan is bounded by <see cref="LoginSessionOptions.SignInTimeout"/>.
    /// </summary>
    private async Task<bool> WaitForLoginSuccessAsync(string containerId, CancellationToken cancellationToken)
    {
        bool successSeen = false;

        await foreach (string line in orchestrator
            .StreamLogsAsync(containerId, cancellationToken)
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

        ClearActiveSession(session);
    }

    /// <summary>
    /// Clears <see cref="_activeSession"/> only when it still refers to <paramref name="session"/>.
    /// Guards against a race where a new session has been started and we would incorrectly
    /// clear the new session instead.
    /// </summary>
    private void ClearActiveSession(LoginSession session)
    {
        lock (_sessionLock)
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
            }
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
