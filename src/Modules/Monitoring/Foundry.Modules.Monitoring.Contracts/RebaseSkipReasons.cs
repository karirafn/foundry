namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The reasons a post-run rebase resolves as <c>skipped</c> (ADR 0087). Every one fails open: the issue
/// moves on to the checks gate with its branch possibly behind the default branch.
/// </summary>
public static class RebaseSkipReasons
{
    /// <summary>The provider accepted the rebase but no new head appeared within the landing bound.</summary>
    public const string Timeout = "timeout";

    /// <summary>Transient provider failures (5xx, timeouts, rate limits) exhausted the retry budget.</summary>
    public const string ProviderError = "provider-error";

    /// <summary>The provider has no rebase implementation yet (GitLab).</summary>
    public const string Unsupported = "unsupported";

    /// <summary>The token may not update the pull request branch.</summary>
    public const string PermissionDenied = "permission-denied";

    /// <summary>The pull request or its branch could not be found.</summary>
    public const string NotFound = "not-found";

    /// <summary>The branch head kept moving between the read and the rebase call.</summary>
    public const string StaleHead = "stale-head";

    /// <summary>The provider refused the rebase with an error the classifier does not recognise.</summary>
    public const string UnrecognisedError = "unrecognised-error";
}
