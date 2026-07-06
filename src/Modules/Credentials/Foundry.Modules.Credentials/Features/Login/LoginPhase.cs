using Foundry.Modules.Credentials.Infrastructure;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Sealed state-variant hierarchy representing the current phase of an OAuth login session.
/// Each variant carries only the data meaningful for that state.
/// Not persisted (TPH not needed — login sessions are transient in-memory state).
/// </summary>
internal abstract record LoginPhase
{
    private protected LoginPhase() { }

    /// <summary>A login container is being started.</summary>
    internal sealed record Starting : LoginPhase
    {
        internal static readonly Starting Instance = new();

        private Starting() { }
    }

    /// <summary>The OAuth URL has been captured; the user must visit it and paste the code.</summary>
    internal sealed record WaitingForAuthorization(string Url) : LoginPhase;

    /// <summary>The code has been delivered; waiting for the container to confirm login.</summary>
    internal sealed record SigningIn : LoginPhase
    {
        internal static readonly SigningIn Instance = new();

        private SigningIn() { }
    }

    /// <summary>Login succeeded and the account identity was captured.</summary>
    internal sealed record Succeeded(AccountIdentity Identity) : LoginPhase;

    /// <summary>Login failed with a typed reason (bad code, timeout, or container non-zero exit).</summary>
    internal sealed record Failed(LoginFailureReason Reason) : LoginPhase;

    /// <summary>
    /// Returns <c>true</c> for phases where the login session is still active
    /// (not yet in a terminal state).
    /// </summary>
    internal bool IsActive =>
        this is Starting or WaitingForAuthorization or SigningIn;
}
