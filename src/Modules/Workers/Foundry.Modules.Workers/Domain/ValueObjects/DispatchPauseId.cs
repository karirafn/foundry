using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

public readonly record struct DispatchPauseId(Guid Value) : IStronglyTypedId<DispatchPauseId>
{
    public static readonly DispatchPauseId Default = new(new Guid("00000000-0000-0000-0000-000000000001"));

    public static DispatchPauseId From(Guid value) => new(value);
}
