namespace Foundry.Modules.Credentials.Features.SessionCheck;

/// <summary>
/// The state of the stored Claude OAuth session, read from the credential volume's
/// <c>.credentials.json</c> without consuming the refresh token.
/// </summary>
internal abstract record OAuthSessionStatus
{
    private OAuthSessionStatus() { }

    /// <summary>
    /// True when the status proves the session is gone and the operator must sign in again.
    /// <see cref="Expired"/> and <see cref="Unreadable"/> are not terminal: the refresh may still
    /// succeed, or the read may have hit a transient problem.
    /// </summary>
    public virtual bool RequiresSignIn => false;

    /// <summary>The access token is unexpired, or carries no expiry at all.</summary>
    public sealed record Active : OAuthSessionStatus;

    /// <summary>
    /// The access token has expired but a refresh token is still stored, so a refresh has not
    /// been attempted yet or failed transiently (e.g. a network error leaves the file untouched).
    /// </summary>
    public sealed record Expired : OAuthSessionStatus;

    /// <summary>
    /// The Claude Code CLI cleared the stored refresh token. The CLI does this when the token
    /// endpoint answers <c>invalid_grant</c>: it writes an empty refresh token, an empty access
    /// token and <c>expiresAt: 0</c>.
    /// </summary>
    public sealed record RefreshRejected : OAuthSessionStatus
    {
        public override bool RequiresSignIn => true;
    }

    /// <summary>The access token has expired and no refresh token is stored to renew it.</summary>
    public sealed record NotRefreshable : OAuthSessionStatus
    {
        public override bool RequiresSignIn => true;
    }

    /// <summary>The credential file, or the <c>claudeAiOauth</c> entry in it, is missing.</summary>
    public sealed record SignedOut : OAuthSessionStatus
    {
        public override bool RequiresSignIn => true;
    }

    /// <summary>The credential file exists but could not be parsed.</summary>
    public sealed record Unreadable : OAuthSessionStatus;
}
