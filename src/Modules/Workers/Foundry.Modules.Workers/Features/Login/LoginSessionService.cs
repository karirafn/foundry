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
    ILogger<LoginSessionService>? logger = null) : ILoginSessionState
{
    internal const string LoginSuccessSignal = "Login successful.";

    private LoginSession? _activeSession;

    public bool IsLoginActive => _activeSession?.IsActive ?? false;

    /// <summary>
    /// Starts a new login session, or returns the existing one if already active (idempotent).
    /// Scans container log output for the OAuth URL and transitions the session to
    /// <see cref="LoginPhase.WaitingForAuthorization"/> when found.
    /// </summary>
    internal async Task<LoginSession> StartAsync(CancellationToken cancellationToken)
    {
        if (_activeSession is not null)
        {
            return _activeSession;
        }

        Result<ContainerId> startResult = await orchestrator.StartLoginContainerAsync(
            new LoginContainerSpec(TimeoutSeconds: (int)LoginSessionOptions.SessionTimeout.TotalSeconds),
            cancellationToken);

        string containerId = startResult is Result<ContainerId>.Success s
            ? s.Value.Value
            : string.Empty;

        LoginSession session = LoginSession.Create(containerId);
        _activeSession = session;

        await ScanForUrlAsync(session, cancellationToken);

        // If the log stream exhausted without a URL, the session never transitioned
        // out of Starting — treat this as a UrlTimeout failure
        if (session.Phase is LoginPhase.Starting)
        {
            await FailSessionAsync(
                session,
                new LoginFailureReason.UrlTimeout("No authorization URL received from the login container."),
                cancellationToken);
        }

        return session;
    }

    /// <summary>
    /// Submits the operator's code to the active login session.
    /// Transitions through <see cref="LoginPhase.SigningIn"/> and then to
    /// <see cref="LoginPhase.Succeeded"/> or <see cref="LoginPhase.Failed"/>.
    /// On failure, teardown is performed and no DB mutation occurs.
    /// </summary>
    internal async Task SubmitCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (_activeSession is null)
        {
            return;
        }

        LoginSession session = _activeSession;

        session.Transition(LoginPhase.SigningIn.Instance);

        try
        {
            await orchestrator.DeliverLoginCodeAsync(session.ContainerId, code, cancellationToken);

            bool loginSuccessful = await WaitForLoginSuccessAsync(session.ContainerId, cancellationToken);

            if (!loginSuccessful)
            {
                await FailSessionAsync(session, new LoginFailureReason.InvalidCode(), cancellationToken);
                return;
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
                return;
            }

            await successCommitter.CommitAsync(identitySuccess.Value, cancellationToken);

            session.Transition(new LoginPhase.Succeeded(identitySuccess.Value));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Unexpected error during login code submission.");
            await FailSessionAsync(session, new LoginFailureReason.Unknown(ex.Message), cancellationToken);
            return;
        }

        await TeardownContainerAsync(session.ContainerId, cancellationToken);
        _activeSession = null;
    }

    private async Task ScanForUrlAsync(LoginSession session, CancellationToken cancellationToken)
    {
        await foreach (string line in orchestrator
            .StreamLogsAsync(session.ContainerId, cancellationToken)
            .ConfigureAwait(false))
        {
            string? url = AuthorizationUrlExtractor.Extract(line);

            if (url is not null)
            {
                session.Transition(new LoginPhase.WaitingForAuthorization(url));
                return;
            }
        }
    }

    /// <summary>
    /// Waits for the login success signal in the log stream and checks the container exit code.
    /// Returns <c>true</c> when the container exits with code 0.
    /// Returns <c>false</c> on non-zero exit (bad or expired code).
    /// </summary>
    private async Task<bool> WaitForLoginSuccessAsync(string containerId, CancellationToken cancellationToken)
    {
        await foreach (string line in orchestrator
            .StreamLogsAsync(containerId, cancellationToken)
            .ConfigureAwait(false))
        {
            if (line.Contains(LoginSuccessSignal, StringComparison.Ordinal))
            {
                break;
            }
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
        session.Transition(new LoginPhase.Failed(reason));
        await TeardownContainerAsync(session.ContainerId, cancellationToken);
        _activeSession = null;
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
