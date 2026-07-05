namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Seam consulted by <see cref="CredentialGate"/> to suppress dispatch while
/// an interactive OAuth login session is active.
/// </summary>
public interface ILoginSessionState
{
    /// <summary>
    /// <c>true</c> while an OAuth login session is in progress; <c>false</c> after
    /// a terminal state or when no session has been started.
    /// </summary>
    bool IsLoginActive { get; }
}
