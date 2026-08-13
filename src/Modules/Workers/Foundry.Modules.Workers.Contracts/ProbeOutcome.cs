namespace Foundry.Modules.Workers.Contracts;

public abstract record ProbeOutcome
{
    private ProbeOutcome() { }

    public sealed record Available : ProbeOutcome;

    public sealed record CreditsStillBlocked : ProbeOutcome;

    public sealed record UsageLimited(DateTimeOffset ResetsAt) : ProbeOutcome;

    public sealed record InfrastructureFailure : ProbeOutcome;
}
