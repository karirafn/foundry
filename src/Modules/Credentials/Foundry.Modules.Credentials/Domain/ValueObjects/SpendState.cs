namespace Foundry.Modules.Credentials.Domain.ValueObjects;

public abstract record SpendState
{
    private SpendState() { }

    public sealed record Available : SpendState;

    public sealed record Blocked(DateTimeOffset NextProbeAt) : SpendState;
}
