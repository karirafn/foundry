using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Workers.Domain;

public static class WorkerRunErrors
{
    public static readonly Error NotFound = new("WorkerRun.NotFound", "Worker run was not found.");
}
