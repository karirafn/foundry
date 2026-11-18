using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Resolves the outcome of a run of one verdict-only role (<see cref="WorkerCapability.Restricted"/>): such a run leaves
/// no branch or merge request, only its verdict. Keyed by role so a new verdict-only role adds an implementation, not a
/// branch in <see cref="WorkerOutcomeResolver"/> (ADR 0098).
/// </summary>
internal interface IVerdictOutcomeResolver
{
    /// <summary>The role whose runs this resolver resolves.</summary>
    WorkerRole Role { get; }

    /// <summary>Resolves a run of <see cref="Role"/> whose container is no longer running.</summary>
    /// <param name="run">The run being resolved.</param>
    /// <param name="status">The exited container's status, or <see langword="null"/> when the container no longer exists.</param>
    /// <param name="containerOutput">The captured container log, when available.</param>
    WorkerOutcome Resolve(IResolvableRun run, WorkerStatus? status, string? containerOutput);
}
