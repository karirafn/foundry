namespace Foundry.Modules.Monitoring.Features.Providers.Checks;

/// <summary>Which CI job, if any, a failing check stands for.</summary>
internal abstract record CiJobLookup
{
    private CiJobLookup()
    {
    }

    /// <summary>The check is the CI job with this provider job ID.</summary>
    internal sealed record Found(long JobId) : CiJobLookup;

    /// <summary>The check is not a CI job the provider exposes logs for.</summary>
    internal sealed record NotACiJob : CiJobLookup;
}
