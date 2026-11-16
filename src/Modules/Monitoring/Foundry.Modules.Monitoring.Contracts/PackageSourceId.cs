using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public readonly record struct PackageSourceId(Guid Value) : IStronglyTypedId<PackageSourceId>
{
    public static PackageSourceId New() => new(Guid.NewGuid());

    public static PackageSourceId From(Guid value) => new(value);
}
