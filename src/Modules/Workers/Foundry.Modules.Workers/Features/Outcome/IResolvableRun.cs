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

    /// <summary>The Implement Demand an implement run was dispatched with; <see langword="null"/> for any other run, or one recorded before runs kept it.</summary>
    ImplementDemand? Demand { get; }

    BranchName? BranchName { get; }

    MonitoredRepositoryId MonitoredRepositoryId { get; }

    /// <summary>The open issues supplied to the run at dispatch; <see langword="null"/> when none were recorded.</summary>
    IReadOnlyList<int>? SuppliedOpenIssueNumbers { get; }

    /// <summary>The facts supplied to a review run at dispatch; <see langword="null"/> for any other run.</summary>
    SuppliedReviewFacts? SuppliedReviewFacts { get; }

    /// <summary>Whether the run was started by Continue without answer, so its <c>needs_input</c> verdict is refused (ADR 0096).</summary>
    bool RefusesNeedsInput { get; }

    /// <summary>The commit the run published to its branch; <see langword="null"/> when it published nothing.</summary>
    CommitSha? PublishedHead { get; }
}
