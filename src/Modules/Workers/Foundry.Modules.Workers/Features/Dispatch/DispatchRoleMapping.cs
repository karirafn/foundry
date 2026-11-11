using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Maps the role an issue's dispatch context carries across from Issues to the <see cref="WorkerRole"/> a run starts
/// under. Exhaustive over <see cref="DispatchRole"/>: a member without a mapping throws, and a unit test covers every
/// member so a missing mapping fails the build's tests, never a dispatch.
/// </summary>
internal static class DispatchRoleMapping
{
    public static WorkerRole ToWorkerRole(DispatchRole role) => role switch
    {
        DispatchRole.Implement => WorkerRole.Implement,
        DispatchRole.Triage => WorkerRole.Triage,
        _ => throw new UnreachableException($"Unhandled DispatchRole: {role}"),
    };
}
