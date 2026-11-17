using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>Whether an awaiting-input round's input has arrived, as the Issue Watch decides it (ADR 0096).</summary>
internal abstract record InputSettlement
{
    private InputSettlement()
    {
    }

    /// <summary>
    /// Every input has outlasted the quiet period: the round arrives with <paramref name="Input"/>, at
    /// <paramref name="ArrivedAt"/>, the newest input's time.
    /// </summary>
    public sealed record Settled(ClarificationInput Input, DateTimeOffset ArrivedAt) : InputSettlement;

    /// <summary>Some input is still inside the quiet period; the last of it settles at <paramref name="Until"/>.</summary>
    public sealed record Waiting(DateTimeOffset Until) : InputSettlement;

    /// <summary>No input since the question was posted.</summary>
    public sealed record NothingNew : InputSettlement;
}
