using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Maps a completed worker run's post-exit facts to a <see cref="WorkerOutcome"/>.
/// Side-effect free: queries the provider (read-only) and returns a decision.
/// All DB writes, container ops, and event dispatch remain in the caller.
/// </summary>
internal sealed class WorkerOutcomeResolver(
    IPostExitProviderQueries postExitProviderQueries,
    IContainerOutputParser containerOutputParser,
    IEnumerable<IVerdictOutcomeResolver> verdictOutcomeResolvers,
    TimeSpan? prRetryDelay = null,
    int prRetryAttempts = WorkerOutcomeResolver.DefaultPrRetryAttempts)
{
    private const int DefaultPrRetryAttempts = 3;

    private static readonly TimeSpan DefaultPrRetryDelay = TimeSpan.FromSeconds(10);

    private const string MergedMissingUrlCode = "MergeRequest.MissingUrl";

    private const string OpenMrMissingUrlCode = "MergeRequest.OpenMissingUrl";

    private const string ClosedMrMessage = "Merge request was closed without being merged";

    private const string NoPrAfterRetriesMessage = "No pull request found after retries";

    internal const string MissingBranchMessage = "Worker run has no branch to resolve its outcome from";

    internal const string UnresolvableRoleMessagePrefix = "Worker role has no outcome resolver";

    internal const string ErrorResultMessage = "Worker reported an error";

    internal const int MaxErrorResultTextLength = 500;

    internal const string OutOfMemoryMessage = "Worker ran out of memory";

    internal const string TerminatedOutsideFoundryMessage = "Worker container was terminated outside Foundry";

    internal const string RemovedOutsideFoundryMessage = "Worker container was removed outside Foundry";

    private readonly TimeSpan _prRetryDelay = prRetryDelay ?? DefaultPrRetryDelay;

    private readonly Dictionary<WorkerRole, IVerdictOutcomeResolver> _verdictOutcomeResolvers =
        verdictOutcomeResolvers.ToDictionary(resolver => resolver.Role);

    /// <summary>
    /// Resolves the outcome of a run whose container is no longer running.
    /// </summary>
    /// <param name="run">The active run being resolved.</param>
    /// <param name="status">
    /// The exited container's status, or <see langword="null"/> when the container no longer exists.
    /// </param>
    /// <param name="containerOutput">The captured container log, when available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<WorkerOutcome> ResolveAsync(
        IResolvableRun run,
        WorkerStatus? status,
        string? containerOutput,
        CancellationToken cancellationToken)
        => ResolveAsync(run, status, containerOutput, verdict: null, cancellationToken);

    /// <summary>
    /// Resolves the outcome as <see cref="ResolveAsync(IResolvableRun, WorkerStatus?, string?, CancellationToken)"/> does,
    /// and yields <see cref="WorkerOutcome.NeedsInput"/> when a run that exited 0 returned a <c>needs_input</c>
    /// <paramref name="verdict"/> (ADR 0096). A merged merge request still completes the issue, a closed one still
    /// fails it, and any other exit code still goes through failure classification.
    /// </summary>
    public async Task<WorkerOutcome> ResolveAsync(
        IResolvableRun run,
        WorkerStatus? status,
        string? containerOutput,
        RunVerdict? verdict,
        CancellationToken cancellationToken)
    {
        // A restricted run leaves no branch or merge request, only its verdict: its role's resolver decides (ADR 0098).
        if (run.Role.Capability == WorkerCapability.Restricted)
        {
            return ResolveVerdictOnly(run, status, containerOutput);
        }

        if (run.BranchName is not { } branchName)
        {
            return new WorkerOutcome.Failure(
                new FailureReason.ContainerError(MissingBranchMessage),
                containerOutput,
                containerOutputParser.ParseRunResultSummary(containerOutput));
        }

        BranchedRun branched = new(run, branchName);
        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);
        bool succeeded = RunFailureClassifier.Succeeded(status, summary);

        // A run started by Continue without answer may not ask again (ADR 0096): its verdict is refused and the facts decide.
        IReadOnlyList<RunVerdictQuestion>? askedQuestions =
            succeeded && verdict is { Status: RunVerdictStatus.NeedsInput } && !run.RefusesNeedsInput ? verdict.Questions : null;

        // Step 5a — MR-state first
        Result<MergeRequestByBranch> mrResult = await postExitProviderQueries.GetMergeRequestByBranchAsync(
            branched.MonitoredRepositoryId,
            branched.BranchName.Value,
            cancellationToken);

        if (mrResult is Result<MergeRequestByBranch>.Failure mrFailure)
        {
            // All MR-LIST failures (including NotFound) are transient — a 404 means repo-not-found
            // (auth/repo error), not "no MR". The provider returns 200 [] for "no MR on this branch".
            return new WorkerOutcome.Indeterminate(mrFailure.Error);
        }

        MergeRequestByBranch mr = ((Result<MergeRequestByBranch>.Success)mrResult).Value;

        return mr.Presence switch
        {
            MergeRequestPresence.Merged => ResolveMerged(branched, mr, containerOutput, summary),

            // An open merge request does not mean the run addressed anything: a revision run that failed must not
            // return its issue to review as if its feedback were handled.
            MergeRequestPresence.Open when succeeded => ResolveOpen(branched, mr, containerOutput, summary, askedQuestions),
            MergeRequestPresence.Open => new WorkerOutcome.ContinuableFailure(
                branched.BranchName,
                RunFailureClassifier.Classify(containerOutputParser, status, containerOutput, summary, run.Role),
                containerOutput,
                summary),
            MergeRequestPresence.Closed => await ResolveClosedAsync(branched, containerOutput, summary, cancellationToken),
            MergeRequestPresence.None => await ResolveNoMrAsync(
                branched,
                status,
                containerOutput,
                summary,
                askedQuestions,
                cancellationToken),
            _ => throw new UnreachableException($"Unknown {nameof(MergeRequestPresence)}: {mr.Presence}"),
        };
    }

    /// <summary>
    /// Returns <see cref="WorkerOutcome.Completed"/> or <see cref="WorkerOutcome.Review"/> when the run's
    /// branch already has a merged or open merge request with a URL; otherwise <see langword="null"/>.
    /// A provider failure is treated as "no merge request" — the caller decides how to proceed.
    /// </summary>
    public async Task<WorkerOutcome?> TryResolveExistingPullRequestAsync(
        IResolvableRun run,
        string? containerOutput,
        RunResultSummary? summary,
        CancellationToken cancellationToken)
    {
        // A run without a branch (a restricted role) has no merge request to find.
        if (run.BranchName is not { } branchName)
        {
            return null;
        }

        BranchedRun branched = new(run, branchName);

        Result<MergeRequestByBranch> mrResult = await postExitProviderQueries.GetMergeRequestByBranchAsync(
            branched.MonitoredRepositoryId,
            branched.BranchName.Value,
            cancellationToken);

        if (mrResult is not Result<MergeRequestByBranch>.Success { Value: { WebUrl: not null } mr })
        {
            return null;
        }

        return mr.Presence switch
        {
            MergeRequestPresence.Merged => ResolveMerged(branched, mr, containerOutput, summary),
            MergeRequestPresence.Open => ResolveOpen(branched, mr, containerOutput, summary, askedQuestions: null),
            _ => null,
        };
    }

    /// <summary>
    /// Delegates a restricted run to the <see cref="IVerdictOutcomeResolver"/> registered for its role. A stored role
    /// no longer in <see cref="WorkerRole.All"/> has none, so its run fails and ends rather than staying active.
    /// </summary>
    private WorkerOutcome ResolveVerdictOnly(IResolvableRun run, WorkerStatus? status, string? containerOutput)
    {
        if (_verdictOutcomeResolvers.TryGetValue(run.Role, out IVerdictOutcomeResolver? resolver))
        {
            return resolver.Resolve(run, status, containerOutput);
        }

        if (WorkerRole.All.Contains(run.Role))
        {
            throw new UnreachableException($"No {nameof(IVerdictOutcomeResolver)} is registered for role '{run.Role.Name}'.");
        }

        return new WorkerOutcome.Failure(
            new FailureReason.ContainerError($"{UnresolvableRoleMessagePrefix}: {run.Role.Name}"),
            containerOutput,
            containerOutputParser.ParseRunResultSummary(containerOutput));
    }

    private static WorkerOutcome ResolveMerged(
        BranchedRun run,
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
        BranchedRun run,
        MergeRequestByBranch mr,
        string? containerOutput,
        RunResultSummary? summary,
        IReadOnlyList<RunVerdictQuestion>? askedQuestions)
    {
        // Guard: Open with null WebUrl — treat as indeterminate; real providers always supply a URL
        if (mr.WebUrl is null)
        {
            return new WorkerOutcome.Indeterminate(
                new Error(OpenMrMissingUrlCode, "Open MR has no web URL; cannot produce a Review outcome."));
        }

        PullRequestUrl pullRequestUrl = PullRequestUrl.From(mr.WebUrl);

        return askedQuestions is null
            ? new WorkerOutcome.Review(run.BranchName, pullRequestUrl, containerOutput, summary)
            : new WorkerOutcome.NeedsInput(run.BranchName, pullRequestUrl, BranchHasCommits: true, askedQuestions, containerOutput, summary);
    }

    private async Task<WorkerOutcome> ResolveClosedAsync(
        BranchedRun run,
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
        BranchedRun run,
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
        BranchedRun run,
        WorkerStatus? status,
        string? containerOutput,
        RunResultSummary? summary,
        IReadOnlyList<RunVerdictQuestion>? askedQuestions,
        CancellationToken cancellationToken)
    {
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

        if (RunFailureClassifier.Succeeded(status, summary))
        {
            return hasCommits
                ? await RepollForMrAsync(run, containerOutput, summary, askedQuestions, cancellationToken)
                : ResolveSucceededNoCommits(run, containerOutput, summary, askedQuestions);
        }

        FailureReason failureReason = RunFailureClassifier.Classify(containerOutputParser, status, containerOutput, summary, run.Role);

        return hasCommits
            ? new WorkerOutcome.ContinuableFailure(run.BranchName, failureReason, containerOutput, summary)
            : new WorkerOutcome.Failure(failureReason, containerOutput, summary);
    }

    private async Task<WorkerOutcome> RepollForMrAsync(
        BranchedRun run,
        string? containerOutput,
        RunResultSummary? summary,
        IReadOnlyList<RunVerdictQuestion>? askedQuestions,
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
                    MergeRequestPresence.Open => ResolveOpen(run, mr, containerOutput, summary, askedQuestions),
                    MergeRequestPresence.Closed => await ResolveClosedAsync(run, containerOutput, summary, cancellationToken),
                    _ => throw new UnreachableException($"Unknown {nameof(MergeRequestPresence)}: {mr.Presence}"),
                };
            }
        }

        // A run that asked before opening a merge request left its commits for a continuation to resume.
        if (askedQuestions is not null)
        {
            return new WorkerOutcome.NeedsInput(run.BranchName, null, BranchHasCommits: true, askedQuestions, containerOutput, summary);
        }

        // Branch was pushed but no MR appeared after retries; preserve the branch so the run is
        // continuable — a ContinuableFailure retains BranchName whereas Failure would abandon it.
        return new WorkerOutcome.ContinuableFailure(
            run.BranchName,
            new FailureReason.ContainerError(NoPrAfterRetriesMessage),
            containerOutput,
            summary);
    }

    private WorkerOutcome ResolveSucceededNoCommits(
        BranchedRun run,
        string? containerOutput,
        RunResultSummary? summary,
        IReadOnlyList<RunVerdictQuestion>? askedQuestions)
    {
        ContainerOutputParseResult parseResult = containerOutputParser.Parse(containerOutput);
        FailureReason? classified = RunFailureClassifier.TryClassify(parseResult, run.Role);

        if (classified is not null)
        {
            return new WorkerOutcome.Failure(classified, containerOutput, summary);
        }

        if (askedQuestions is not null)
        {
            return new WorkerOutcome.NeedsInput(run.BranchName, null, BranchHasCommits: false, askedQuestions, containerOutput, summary);
        }

        // Exit 0 with no commits — the issue is unchanged (nothing to submit)
        return new WorkerOutcome.Unchanged(run.BranchName, containerOutput, summary);
    }

    /// <summary>A run resolved through its branch: the implement paths, which look up its merge request and commits.</summary>
    private readonly record struct BranchedRun(IResolvableRun Run, BranchName BranchName)
    {
        public MonitoredRepositoryId MonitoredRepositoryId => Run.MonitoredRepositoryId;

        public WorkerRole Role => Run.Role;
    }
}
