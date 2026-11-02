namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Outcome of an on-demand usage refresh request (<see cref="IUsagePollCoordinator.RequestRefreshAsync"/>).
/// </summary>
internal enum UsageRefreshOutcome
{
    /// <summary>A new poll was started in the background.</summary>
    Started,

    /// <summary>A poll was already in flight; the request joined it and no second poll started.</summary>
    InFlight,

    /// <summary>The last successful snapshot is younger than the refresh floor; no poll started.</summary>
    AlreadyCurrent,

    /// <summary>The account cannot report usage (no account, API-key mode, or invalid credentials).</summary>
    Unavailable,

    /// <summary>An interactive login session is active; no poll started.</summary>
    LoginActive,
}
