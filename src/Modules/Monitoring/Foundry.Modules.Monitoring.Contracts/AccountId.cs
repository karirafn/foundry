using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public readonly record struct AccountId(Guid Value) : IStronglyTypedId<AccountId>
{
    public static AccountId New() => new(Guid.NewGuid());

    public static AccountId From(Guid value) => new(value);
}
