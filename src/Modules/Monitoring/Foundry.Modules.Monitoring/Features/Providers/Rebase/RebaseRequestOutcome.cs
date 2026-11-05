namespace Foundry.Modules.Monitoring.Features.Providers.Rebase;

/// <summary>
/// The provider's answer to a server-side rebase request (ADR 0087). Transient failures (5xx, timeouts,
/// rate limits) are a <c>Result</c> failure instead; every variant here is a definite answer.
/// </summary>
internal abstract record RebaseRequestOutcome
{
    private RebaseRequestOutcome()
    {
    }

    /// <summary>The provider accepted the rebase; it lands asynchronously as a new head.</summary>
    public sealed record Requested : RebaseRequestOutcome;

    /// <summary>The base branch has no new commits; nothing to rebase.</summary>
    public sealed record UpToDate : RebaseRequestOutcome;

    /// <summary>The rebase conflicts with the base branch.</summary>
    public sealed record Conflict : RebaseRequestOutcome;

    /// <summary>The expected head no longer matches the branch head — a push landed in between.</summary>
    public sealed record StaleHead : RebaseRequestOutcome;

    /// <summary>A permanent refusal or an unrecognised error; <see cref="Reason"/> is a skip reason.</summary>
    public sealed record Refused(string Reason) : RebaseRequestOutcome;

    /// <summary>The provider has no rebase implementation.</summary>
    public sealed record Unsupported : RebaseRequestOutcome;
}
