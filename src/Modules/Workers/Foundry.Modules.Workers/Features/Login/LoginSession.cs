namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Holds the in-memory state for a single OAuth login session.
/// At most one session is active at a time — <see cref="LoginSessionService"/> enforces this.
/// </summary>
internal sealed class LoginSession
{
    private LoginPhase _phase;

    private LoginSession(Guid sessionId, LoginPhase phase)
    {
        SessionId = sessionId;
        _phase = phase;
    }

    internal Guid SessionId { get; }

    internal LoginPhase Phase => _phase;

    internal static LoginSession Create() =>
        new(Guid.NewGuid(), LoginPhase.Starting.Instance);

    internal void Transition(LoginPhase next) => _phase = next;

    internal bool IsActive => _phase.IsActive;
}
