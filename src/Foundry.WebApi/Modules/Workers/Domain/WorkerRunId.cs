using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Workers.Domain;

public readonly record struct WorkerRunId(Guid Value) : IStronglyTypedId<WorkerRunId>
{
    public static WorkerRunId New() => new(Guid.NewGuid());

    public static WorkerRunId From(Guid value) => new(value);
}
