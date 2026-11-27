namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

internal enum UnknownReason
{
    /// <summary>A transient network or transport error prevented the probe from completing.</summary>
    Transport = 0,

    /// <summary>The probe 403'd because the GitHub API rate limit was exhausted.</summary>
    RateLimited = 1,

    /// <summary>
    /// The provider refused the account token when a run used it, so a <see cref="WriteProbeVerdict.Granted"/>
    /// verdict was reset to be probed again on the next poll.
    /// </summary>
    AccessRefused = 2,
}
