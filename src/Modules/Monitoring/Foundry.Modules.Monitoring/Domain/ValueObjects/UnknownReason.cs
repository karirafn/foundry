namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

internal enum UnknownReason
{
    /// <summary>A transient network or transport error prevented the probe from completing.</summary>
    Transport = 0,

    /// <summary>The probe 403'd because the GitHub API rate limit was exhausted.</summary>
    RateLimited = 1,
}
