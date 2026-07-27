using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features;

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

    private readonly TimeSpan _prRetryDelay = prRetryDelay ?? DefaultPrRetryDelay;

    public async Task<WorkerOutcome> ResolveAsync(
        ActiveRun run,
        int? exitCode,
        string? containerOutput,
        int defaultCooldownMinutes,
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
            MergeRequestPresence.Merged => ResolveMerged(run, mr, summary),
            MergeRequestPresence.Open => ResolveOpen(run, mr, summary),
            MergeRequestPresence.Closed => await ResolveClosedAsync(run, containerOutput, summary, cancellationToken),
            MergeRequestPresence.None => await ResolveNoMrAsync(
                run,
                exitCode,
                containerOutput,
                defaultCooldownMinutes,
                cancellationToken),
            _ => throw new UnreachableException($"Unknown {nameof(MergeRequestPresence)}: {mr.Presence}"),
        };
    }

    private static WorkerOutcome ResolveMerged(ActiveRun run, MergeRequestByBranch mr, RunResultSummary? summary)
    {
        // Guard: Merged with null WebUrl — never build a CompletedIssue without a PR URL
        if (mr.WebUrl is null)
        {
            return new WorkerOutcome.Indeterminate(
                new Error(MergedMissingUrlCode, "Merged MR has no web URL; cannot complete the issue."));
        }

        return new WorkerOutcome.Completed(run.BranchName, PullRequestUrl.From(mr.WebUrl), summary);
    }

    private static WorkerOutcome ResolveOpen(ActiveRun run, MergeRequestByBranch mr, RunResultSummary? summary)
    {
        // Guard: Open with null WebUrl — treat as indeterminate; real providers always supply a URL
        if (mr.WebUrl is null)
        {
            return new WorkerOutcome.Indeterminate(
                new Error(OpenMrMissingUrlCode, "Open MR has no web URL; cannot produce a Review outcome."));
        }

        return new WorkerOutcome.Review(run.BranchName, PullRequestUrl.From(mr.WebUrl), summary);
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
        Result<bool> commitsResult = await postExitProviderQueries.HasBranchCommitsAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        if (commitsResult is Result<bool>.Failure commitsFailure)
        {
            if (commitsFailure.Error.Kind == ErrorKind.NotFound)
            {
                // NotFound definitively means no commits
                return new WorkerOutcome.Failure(failureReason, containerOutput, summary);
            }

            return new WorkerOutcome.Indeterminate(commitsFailure.Error);
        }

        bool hasCommits = ((Result<bool>.Success)commitsResult).Value;

        return hasCommits
            ? new WorkerOutcome.ContinuableFailure(run.BranchName, failureReason, containerOutput, summary)
            : new WorkerOutcome.Failure(failureReason, containerOutput, summary);
    }

    private async Task<WorkerOutcome> ResolveNoMrAsync(
        ActiveRun run,
        int? exitCode,
        string? containerOutput,
        int defaultCooldownMinutes,
        CancellationToken cancellationToken)
    {
        // Step 5b — no MR fallback
        Result<bool> commitsResult = await postExitProviderQueries.HasBranchCommitsAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        bool hasCommits;

        if (commitsResult is Result<bool>.Failure commitsFailure)
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
            hasCommits = ((Result<bool>.Success)commitsResult).Value;
        }

        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);

        if (exitCode == 0 && hasCommits)
        {
            return await RepollForMrAsync(run, containerOutput, summary, cancellationToken);
        }

        if (exitCode == 0 && !hasCommits)
        {
            return ResolveExitZeroNoCommits(run, containerOutput, defaultCooldownMinutes, summary);
        }

        // exit != 0 or null exit
        ContainerOutputParseResult parseResult = containerOutputParser.Parse(containerOutput, defaultCooldownMinutes);
        FailureReason failureReason = ClassifyNonZeroFailureReason(parseResult, exitCode);

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
                    MergeRequestPresence.Merged => ResolveMerged(run, mr, summary),
                    MergeRequestPresence.Open => ResolveOpen(run, mr, summary),
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
        int defaultCooldownMinutes,
        RunResultSummary? summary)
    {
        ContainerOutputParseResult parseResult = containerOutputParser.Parse(containerOutput, defaultCooldownMinutes);

        if (parseResult is ContainerOutputParseResult.UsageLimited usageLimited)
        {
            return new WorkerOutcome.Failure(new FailureReason.UsageLimited(usageLimited.ResetsAt), containerOutput, summary);
        }

        // Exit 0 with no commits — the issue is unchanged (nothing to submit)
        return new WorkerOutcome.Unchanged(run.BranchName, summary);
    }

    private static FailureReason ClassifyNonZeroFailureReason(
        ContainerOutputParseResult parseResult,
        int? exitCode)
    {
        if (parseResult is ContainerOutputParseResult.WorkerBootstrapFailed bootstrapFailed)
        {
            return new FailureReason.WorkerBootstrapFailed(SecretRedactor.Redact(bootstrapFailed.Detail));
        }

        if (parseResult is ContainerOutputParseResult.UsageLimited usageLimited)
        {
            return new FailureReason.UsageLimited(usageLimited.ResetsAt);
        }

        if (parseResult is ContainerOutputParseResult.AuthInvalid)
        {
            return new FailureReason.AuthInvalid();
        }

        // NoResultLine with non-zero exit = bootstrap failure (no result line produced)
        if (parseResult is ContainerOutputParseResult.NoResultLine)
        {
            return new FailureReason.WorkerBootstrapFailed(HeuristicBootstrapDetail);
        }

        return new FailureReason.NonZeroExit(exitCode ?? -1);
    }
}
