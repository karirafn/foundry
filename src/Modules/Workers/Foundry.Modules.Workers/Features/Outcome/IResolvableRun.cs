using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// What outcome resolution reads from a run: a running run Foundry stopped or found gone, or a publishing run whose
/// container already ended (ADR 0100).
/// </summary>
internal interface IResolvableRun
{
    WorkerRunId Id { get; }

    WorkerRole Role { get; }

    BranchName? BranchName { get; }

    MonitoredRepositoryId MonitoredRepositoryId { get; }
}
