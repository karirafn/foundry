using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain;

internal static class WorkerRunErrors
{
    public static readonly Error NotFound = new("WorkerRun.NotFound", "Worker run was not found.");
}
