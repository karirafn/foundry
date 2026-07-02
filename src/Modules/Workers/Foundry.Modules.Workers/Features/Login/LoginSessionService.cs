namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Singleton service that manages a single in-memory OAuth login session.
/// Implements <see cref="ILoginSessionState"/> so dispatch can consult it without
/// a dependency on the full service.
/// </summary>
/// <remarks>
/// Step 3 will extend this to start a real login container via
/// <c>IWorkerOrchestrator.StartLoginContainerAsync</c>. For step 1, the orchestrator
/// seam is reached through the existing <see cref="IWorkerOrchestrator.StreamLogsAsync"/>
/// (the fake supplies scripted log lines) so the state-machine tracer is unit-testable
/// with zero Docker.
/// </remarks>
internal sealed class LoginSessionService(IWorkerOrchestrator orchestrator) : ILoginSessionState
{
    private LoginSession? _activeSession;

    public bool IsLoginActive => _activeSession?.IsActive ?? false;

    /// <summary>
    /// Starts a new login session, or returns the existing one if already active (idempotent).
    /// Scans container log output for the OAuth URL and transitions the session to
    /// <see cref="LoginPhase.WaitingForAuthorization"/> when found.
    /// </summary>
    /// <remarks>
    /// This method awaits the URL scan before returning so that the session phase reflects
    /// the outcome of the scan. Step 7 will wrap this in a background task so the HTTP
    /// endpoint can return 202 immediately after the container starts.
    /// </remarks>
    internal async Task<LoginSession> StartAsync(CancellationToken cancellationToken)
    {
        if (_activeSession is not null)
        {
            return _activeSession;
        }

        LoginSession session = LoginSession.Create();
        _activeSession = session;

        await ScanForUrlAsync(session, cancellationToken);

        return session;
    }

    private async Task ScanForUrlAsync(LoginSession session, CancellationToken cancellationToken)
    {
        // Step 3 will replace the placeholder container ID with the real ID returned
        // from StartLoginContainerAsync. For step 1, the fake orchestrator's StreamLogsAsync
        // returns scripted lines regardless of the container ID.
        const string PlaceholderContainerId = "";

        await foreach (string line in orchestrator
            .StreamLogsAsync(PlaceholderContainerId, cancellationToken)
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
}
