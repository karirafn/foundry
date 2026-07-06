namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Holds the in-memory state for a single OAuth login session.
/// At most one session is active at a time — <see cref="LoginSessionService"/> enforces this.
/// </summary>
internal sealed class LoginSession
{
    private LoginPhase _phase;

    private LoginSession(Guid sessionId, string containerId, LoginPhase phase)
    {
        SessionId = sessionId;
        ContainerId = containerId;
        _phase = phase;
    }

    internal Guid SessionId { get; }

    /// <summary>Docker container ID of the active login container.</summary>
    internal string ContainerId { get; private set; }

    internal LoginPhase Phase => _phase;

    internal static LoginSession Create(string containerId) =>
        new(Guid.NewGuid(), containerId, LoginPhase.Starting.Instance);

    internal void Transition(LoginPhase next) => _phase = next;

    /// <summary>
    /// Sets the container ID once the container has been started asynchronously.
    /// Called by <see cref="LoginSessionService"/> after the background start completes.
    /// </summary>
    internal void SetContainerId(string containerId) => ContainerId = containerId;

    internal bool IsActive => _phase.IsActive;
}
