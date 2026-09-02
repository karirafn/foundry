using System.Text.Json.Serialization;

using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

[JsonConverter(typeof(WorkerRunIdJsonConverter))]
public readonly record struct WorkerRunId(Guid Value) : IStronglyTypedId<WorkerRunId>
{
    public static WorkerRunId New() => new(Guid.NewGuid());

    public static WorkerRunId From(Guid value) => new(value);
}
