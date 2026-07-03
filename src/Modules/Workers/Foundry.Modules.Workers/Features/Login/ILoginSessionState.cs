namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Seam consulted by <c>WorkerDispatchService</c> to suppress dispatch while
/// an interactive OAuth login session is active.
/// </summary>
public interface ILoginSessionState
{
    /// <summary>
    /// <c>true</c> while an OAuth login session is in progress
    /// (phase is <see cref="LoginPhase.Starting"/>, <see cref="LoginPhase.WaitingForAuthorization"/>,
    /// or <see cref="LoginPhase.SigningIn"/>); <c>false</c> after a terminal state
    /// (<see cref="LoginPhase.Succeeded"/> or <see cref="LoginPhase.Failed"/>), or when
    /// no session has been started.
    /// </summary>
    bool IsLoginActive { get; }
}
