namespace Foundry.Modules.Credentials.Features.SessionCheck;

/// <summary>
/// Verifies the stored Claude OAuth session and invalidates the account when the session can no
/// longer be renewed, so dispatch pauses before a worker is sent out with a dead login.
/// </summary>
internal interface IOAuthSessionCheckCoordinator
{
    /// <summary>
    /// Runs one session check. Returns the observed status, or <c>null</c> when the check was
    /// skipped (no OAuth account, account already invalid, login in progress, a check already in
    /// flight) or could not complete (Docker error).
    /// </summary>
    Task<OAuthSessionStatus?> CheckAsync(CancellationToken cancellationToken);
}
