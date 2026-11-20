namespace Foundry.Modules.Credentials.Domain.ValueObjects;

public abstract record SpendState
{
    private SpendState() { }

    public sealed record Available : SpendState;

    // A credit block has no known reset time, so the next probe is persisted here: an in-memory schedule would
    // leave a blocked account with no probe after a host restart.
    public sealed record Blocked(DateTimeOffset NextProbeAt) : SpendState;
}
