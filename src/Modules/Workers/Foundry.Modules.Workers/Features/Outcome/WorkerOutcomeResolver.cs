using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Maps a completed worker run's post-exit facts to a <see cref="WorkerOutcome"/>.
/// Side-effect free: queries the provider (read-only) and returns a decision.
/// All DB writes, container ops, and event dispatch remain in the caller.
/// </summary>
internal sealed class WorkerOutcomeResolver(
    IPostExitProviderQueries postExitProviderQueries,
    IContainerOutputParser containerOutputParser,
    TimeSpan? prRetryDelay = null,
    int prRetryAttempts = WorkerOutcomeResolver.DefaultPrRetryAttempts)
{
    private const int DefaultPrRetryAttempts = 3;

    private static readonly TimeSpan DefaultPrRetryDelay = TimeSpan.FromSeconds(10);

    private const string HeuristicBootstrapDetail =
        "stage=unknown (no result line; container terminated before producing output)";

    private const string MergedMissingUrlCode = "MergeRequest.MissingUrl";

    private const string OpenMrMissingUrlCode = "MergeRequest.OpenMissingUrl";

    private const string ClosedMrMessage = "Merge request was closed without being merged";

    private const string NoPrAfterRetriesMessage = "No pull request found after retries";

    internal const string ErrorResultMessage = "Worker reported an error";

    internal const int MaxErrorResultTextLength = 500;

    internal const string OutOfMemoryMessage = "Worker ran out of memory";

    internal const string TerminatedOutsideFoundryMessage = "Worker container was terminated outside Foundry";

    internal const string RemovedOutsideFoundryMessage = "Worker container was removed outside Foundry";

    // SIGKILL (128 + 9) and SIGTERM (128 + 15): a docker kill/stop issued outside Foundry.
    private const int SigKillExitCode = 137;

    private const int SigTermExitCode = 143;

    private readonly TimeSpan _prRetryDelay = prRetryDelay ?? DefaultPrRetryDelay;

    /// <summary>
    /// Resolves the outcome of a run whose container is no longer running.
    /// </summary>
    /// <param name="run">The active run being resolved.</param>
    /// <param name="status">
    /// The exited container's status, or <see langword="null"/> when the container no longer exists.
    /// </param>
    /// <param name="containerOutput">The captured container log, when available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<WorkerOutcome> ResolveAsync(
        ActiveRun run,
        WorkerStatus? status,
        string? containerOutput,
        CancellationToken cancellationToken)
    {
        // Step 5a — MR-state first
        Result<MergeRequestByBranch> mrResult = await postExitProviderQueries.GetMergeRequestByBranchAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        if (mrResult is Result<MergeRequestByBranch>.Failure mrFailure)
        {
            // All MR-LIST failures (including NotFound) are transient — a 404 means repo-not-found
            // (auth/repo error), not "no MR". The provider returns 200 [] for "no MR on this branch".
            return new WorkerOutcome.Indeterminate(mrFailure.Error);
        }

        MergeRequestByBranch mr = ((Result<MergeRequestByBranch>.Success)mrResult).Value;
        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);

        return mr.Presence switch
        {
            MergeRequestPresence.Merged => ResolveMerged(run, mr, containerOutput, summary),
            MergeRequestPresence.Open => ResolveOpen(run, mr, containerOutput, summary),
            MergeRequestPresence.Closed => await ResolveClosedAsync(run, containerOutput, summary, cancellationToken),
            MergeRequestPresence.None => await ResolveNoMrAsync(run, status, containerOutput, cancellationToken),
            _ => throw new UnreachableException($"Unknown {nameof(MergeRequestPresence)}: {mr.Presence}"),
        };
    }

    /// <summary>
    /// Returns <see cref="WorkerOutcome.Completed"/> or <see cref="WorkerOutcome.Review"/> when the run's
    /// branch already has a merged or open merge request with a URL; otherwise <see langword="null"/>.
    /// A provider failure is treated as "no merge request" — the caller decides how to proceed.
    /// </summary>
    public async Task<WorkerOutcome?> TryResolveExistingPullRequestAsync(
        ActiveRun run,
        string? containerOutput,
        RunResultSummary? summary,
        CancellationToken cancellationToken)
    {
        Result<MergeRequestByBranch> mrResult = await postExitProviderQueries.GetMergeRequestByBranchAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        if (mrResult is not Result<MergeRequestByBranch>.Success { Value: { WebUrl: not null } mr })
        {
            return null;
        }

        return mr.Presence switch
        {
            MergeRequestPresence.Merged => ResolveMerged(run, mr, containerOutput, summary),
            MergeRequestPresence.Open => ResolveOpen(run, mr, containerOutput, summary),
            _ => null,
        };
    }

    private static WorkerOutcome ResolveMerged(
        ActiveRun run,
        MergeRequestByBranch mr,
        string? containerOutput,
        RunResultSummary? summary)
    {
        // Guard: Merged with null WebUrl — never build a CompletedIssue without a PR URL
        if (mr.WebUrl is null)
        {
            return new WorkerOutcome.Indeterminate(
                new Error(MergedMissingUrlCode, "Merged MR has no web URL; cannot complete the issue."));
        }

        return new WorkerOutcome.Completed(run.BranchName, PullRequestUrl.From(mr.WebUrl), containerOutput, summary);
    }

    private static WorkerOutcome ResolveOpen(
        ActiveRun run,
        MergeRequestByBranch mr,
        string? containerOutput,
        RunResultSummary? summary)
    {
        // Guard: Open with null WebUrl — treat as indeterminate; real providers always supply a URL
        if (mr.WebUrl is null)
        {
            return new WorkerOutcome.Indeterminate(
                new Error(OpenMrMissingUrlCode, "Open MR has no web URL; cannot produce a Review outcome."));
        }

        return new WorkerOutcome.Review(run.BranchName, PullRequestUrl.From(mr.WebUrl), containerOutput, summary);
    }

    private async Task<WorkerOutcome> ResolveClosedAsync(
        ActiveRun run,
        string? containerOutput,
        RunResultSummary? summary,
        CancellationToken cancellationToken)
    {
        // MR was closed without merging — check commits to determine ContinuableFailure vs Failure
        return await ResolveByCommitsAsync(
            run,
            containerOutput,
            summary,
            new FailureReason.ContainerError(ClosedMrMessage),
            cancellationToken);
    }

    /// <summary>
    /// Queries branch commits and returns <see cref="WorkerOutcome.ContinuableFailure"/> when commits exist,
    /// <see cref="WorkerOutcome.Failure"/> when none exist, or <see cref="WorkerOutcome.Indeterminate"/>
    /// when the query fails with a non-NotFound error.
    /// </summary>
    private async Task<WorkerOutcome> ResolveByCommitsAsync(
        ActiveRun run,
        string? containerOutput,
        RunResultSummary? summary,
        FailureReason failureReason,
        CancellationToken cancellationToken)
    {
        Result<BranchCommitSummary> commitsResult = await postExitProviderQueries.GetBranchCommitSummaryAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        if (commitsResult is Result<BranchCommitSummary>.Failure commitsFailure)
        {
            if (commitsFailure.Error.Kind == ErrorKind.NotFound)
            {
                // NotFound definitively means no commits
                return new WorkerOutcome.Failure(failureReason, containerOutput, summary);
            }

            return new WorkerOutcome.Indeterminate(commitsFailure.Error);
        }

        bool hasCommits = ((Result<BranchCommitSummary>.Success)commitsResult).Value.CommitCount > 0;

        return hasCommits
            ? new WorkerOutcome.ContinuableFailure(run.BranchName, failureReason, containerOutput, summary)
            : new WorkerOutcome.Failure(failureReason, containerOutput, summary);
    }

    private async Task<WorkerOutcome> ResolveNoMrAsync(
        ActiveRun run,
        WorkerStatus? status,
        string? containerOutput,
        CancellationToken cancellationToken)
    {
        int? exitCode = status?.ExitCode;

        // Step 5b — no MR fallback
        Result<BranchCommitSummary> commitsResult = await postExitProviderQueries.GetBranchCommitSummaryAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        bool hasCommits;

        if (commitsResult is Result<BranchCommitSummary>.Failure commitsFailure)
        {
            if (commitsFailure.Error.Kind == ErrorKind.NotFound)
            {
                // NotFound definitively means no commits
                hasCommits = false;
            }
            else
            {
                // Transient error — caller retries
                return new WorkerOutcome.Indeterminate(commitsFailure.Error);
            }
        }
        else
        {
            hasCommits = ((Result<BranchCommitSummary>.Success)commitsResult).Value.CommitCount > 0;
        }

        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);

        if (exitCode == 0 && hasCommits)
        {
            return await RepollForMrAsync(run, containerOutput, summary, cancellationToken);
        }

        if (exitCode == 0 && !hasCommits)
        {
            return ResolveExitZeroNoCommits(run, containerOutput, summary);
        }

        // exit != 0, or the container is gone
        ContainerOutputParseResult parseResult = containerOutputParser.Parse(containerOutput);
        FailureReason failureReason = ClassifyNonZeroFailureReason(parseResult, status, run.Role);

        return hasCommits
            ? new WorkerOutcome.ContinuableFailure(run.BranchName, failureReason, containerOutput, summary)
            : new WorkerOutcome.Failure(failureReason, containerOutput, summary);
    }

    private async Task<WorkerOutcome> RepollForMrAsync(
        ActiveRun run,
        string? containerOutput,
        RunResultSummary? summary,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < prRetryAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(_prRetryDelay, cancellationToken);
            }

            Result<MergeRequestByBranch> mrResult = await postExitProviderQueries.GetMergeRequestByBranchAsync(
                run.MonitoredRepositoryId,
                run.BranchName.Value,
                cancellationToken);

            if (mrResult is Result<MergeRequestByBranch>.Success { Value: { Presence: not MergeRequestPresence.None } mr })
            {
                // Delegate to the same presence switch as ResolveAsync — a Merged MR must complete,
                // a Closed MR must go through the commit-check path, only Open yields Review.
                return mr.Presence switch
                {
                    MergeRequestPresence.Merged => ResolveMerged(run, mr, containerOutput, summary),
                    MergeRequestPresence.Open => ResolveOpen(run, mr, containerOutput, summary),
                    MergeRequestPresence.Closed => await ResolveClosedAsync(run, containerOutput, summary, cancellationToken),
                    _ => throw new UnreachableException($"Unknown {nameof(MergeRequestPresence)}: {mr.Presence}"),
                };
            }
        }

        // Branch was pushed but no MR appeared after retries; preserve the branch so the run is
        // continuable — a ContinuableFailure retains BranchName whereas Failure would abandon it.
        return new WorkerOutcome.ContinuableFailure(
            run.BranchName,
            new FailureReason.ContainerError(NoPrAfterRetriesMessage),
            containerOutput,
            summary);
    }

    private WorkerOutcome ResolveExitZeroNoCommits(
        ActiveRun run,
        string? containerOutput,
        RunResultSummary? summary)
    {
        ContainerOutputParseResult parseResult = containerOutputParser.Parse(containerOutput);
        FailureReason? classified = TryClassifyParseResult(parseResult, run.Role);

        if (classified is not null)
        {
            return new WorkerOutcome.Failure(classified, containerOutput, summary);
        }

        // The CLI can exit 0 while its result line reports is_error (e.g. an expired OAuth session).
        // Such a run did no work; recording it as Unchanged would hide the failure from retry.
        if (summary is { IsError: true })
        {
            return new WorkerOutcome.Failure(ErrorResultFailureReason(summary), containerOutput, summary);
        }

        // Exit 0 with no commits — the issue is unchanged (nothing to submit)
        return new WorkerOutcome.Unchanged(run.BranchName, containerOutput, summary);
    }

    /// <summary>
    /// Builds the generic failure reason for an exit-0 run whose result line reports
    /// <c>is_error</c> but whose output matches no known failure category.
    /// </summary>
    private static FailureReason.ContainerError ErrorResultFailureReason(RunResultSummary summary)
    {
        string? resultText = summary.ResultText?.Trim();

        if (string.IsNullOrEmpty(resultText))
        {
            return new FailureReason.ContainerError(ErrorResultMessage);
        }

        string redacted = SecretRedactor.Redact(resultText).ReplaceLineEndings(" ");

        if (redacted.Length > MaxErrorResultTextLength)
        {
            redacted = redacted[..MaxErrorResultTextLength];
        }

        return new FailureReason.ContainerError($"{ErrorResultMessage}: {redacted}");
    }

    private static FailureReason ClassifyNonZeroFailureReason(
        ContainerOutputParseResult parseResult,
        WorkerStatus? status,
        WorkerRole role)
    {
        if (TryClassifyParseResult(parseResult, role) is { } classified)
        {
            return classified;
        }

        if (parseResult is ContainerOutputParseResult.NoResultLine)
        {
            return ClassifyNoResultLine(status);
        }

        return new FailureReason.NonZeroExit(status?.ExitCode ?? -1);
    }

    /// <summary>
    /// Maps a parse result that identifies a specific failure category to its
    /// <see cref="FailureReason"/>; returns <see langword="null"/> when the parse carries no category.
    /// Shared by the non-zero and the exit-0 paths so both classify a failure identically.
    /// </summary>
    private static FailureReason? TryClassifyParseResult(ContainerOutputParseResult parseResult, WorkerRole role)
    {
        return parseResult switch
        {
            ContainerOutputParseResult.WorkerBootstrapFailed bootstrapFailed =>
                new FailureReason.WorkerBootstrapFailed(SecretRedactor.Redact(bootstrapFailed.Detail)),
            ContainerOutputParseResult.UsageLimited usageLimited => new FailureReason.UsageLimited(usageLimited.ResetsAt),
            ContainerOutputParseResult.CreditsExhausted => new FailureReason.CreditsExhausted(),
            ContainerOutputParseResult.AuthInvalid => new FailureReason.AuthInvalid(),
            ContainerOutputParseResult.TransientApiError => new FailureReason.TransientApiError(),
            ContainerOutputParseResult.ModelUnavailable => new FailureReason.ModelUnavailable(role.Name),
            ContainerOutputParseResult.TurnLimitReached => new FailureReason.TurnLimitReached(),
            _ => null,
        };
    }

    /// <summary>
    /// Classifies a run that ended without a result line. Containers killed or removed outside
    /// Foundry are container errors; anything else is a bootstrap failure (no result line produced).
    /// </summary>
    private static FailureReason ClassifyNoResultLine(WorkerStatus? status)
    {
        if (status is null)
        {
            return new FailureReason.ContainerError(RemovedOutsideFoundryMessage);
        }

        // OOM is checked first: the kernel's OOM kill also surfaces as exit 137.
        if (status.OomKilled)
        {
            return new FailureReason.ContainerError(OutOfMemoryMessage);
        }

        if (status.ExitCode is SigKillExitCode or SigTermExitCode)
        {
            return new FailureReason.ContainerError(TerminatedOutsideFoundryMessage);
        }

        return new FailureReason.WorkerBootstrapFailed(HeuristicBootstrapDetail);
    }
}
