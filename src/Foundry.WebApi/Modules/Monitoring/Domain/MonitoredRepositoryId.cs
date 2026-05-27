using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Monitoring.Domain;

public readonly record struct MonitoredRepositoryId(Guid Value) : IStronglyTypedId<MonitoredRepositoryId>
{
    public static MonitoredRepositoryId New() => new(Guid.NewGuid());

    public static MonitoredRepositoryId From(Guid value) => new(value);
}
