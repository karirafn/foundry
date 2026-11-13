using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>
/// What Publish pushes (ADR 0100): the Run bundle on the run's Hand-off volume, checked against the base Foundry
/// persisted at Prepare (never read from the volume), rewritten to <paramref name="Identity"/>, onto
/// <paramref name="RunBranch"/> only. The scan suppresses exactly <paramref name="ConfirmedFindings"/>, the findings an
/// operator confirmed as false positives (#899).
/// </summary>
internal sealed record PublishRequest(
    WorkerRunId RunId,
    Uri CloneUrl,
    string GitPat,
    BranchName RunBranch,
    RunBase Base,
    CommitIdentity Identity,
    WorkerResourceLimitsSnapshot Limits,
    IReadOnlyList<SecretFinding> ConfirmedFindings);
