using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Monitoring.Domain;

public readonly record struct AccountId(Guid Value) : IStronglyTypedId<AccountId>
{
    public static AccountId New() => new(Guid.NewGuid());

    public static AccountId From(Guid value) => new(value);
}
