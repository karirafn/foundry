using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain;

public readonly record struct WorkerReportId(Guid Value) : IStronglyTypedId<WorkerReportId>
{
    public static WorkerReportId New() => new(Guid.NewGuid());

    public static WorkerReportId From(Guid value) => new(value);
}
