using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Verdicts;
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

    internal const string MissingBranchMessage = "Worker run has no branch to resolve its outcome from";

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
        // The outcome step dispatches by role: a triage run leaves no branch or merge request, only its verdict.
        if (run.Role == WorkerRole.Triage)
        {
            return ResolveTriage(run, status, containerOutput);
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
        bool succeeded = Succeeded(status, summary);

        IReadOnlyList<RunVerdictQuestion>? askedQuestions =
            succeeded && verdict is { Status: RunVerdictStatus.NeedsInput } ? verdict.Questions : null;

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
                ClassifyFailure(status, containerOutput, summary, run.Role),
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
    /// Resolves a triage run (ADR 0097) from its exit and its verdict alone: it has no branch, so the merge-request
    /// lookup never runs. Only a run that exited 0 with a schema-valid verdict completes; a missing or invalid verdict
    /// is a step failure (ADR 0094), and any other exit is classified as for every run.
    /// </summary>
    private WorkerOutcome ResolveTriage(IResolvableRun run, WorkerStatus? status, string? containerOutput)
    {
        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);

        if (!Succeeded(status, summary))
        {
            return new WorkerOutcome.Failure(ClassifyFailure(status, containerOutput, summary, run.Role), containerOutput, summary);
        }

        if (TryClassifyParseResult(containerOutputParser.Parse(containerOutput), run.Role) is { } classified)
        {
            return new WorkerOutcome.Failure(classified, containerOutput, summary);
        }

        if (containerOutputParser.ParseStructuredOutput(containerOutput) is not { } structuredOutput)
        {
            return new WorkerOutcome.Failure(
                new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.MissingDetail),
                containerOutput,
                summary);
        }

        if (TriageVerdict.TryParse(structuredOutput) is not { } verdict)
        {
            return new WorkerOutcome.Failure(
                new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.InvalidDetail),
                containerOutput,
                summary);
        }

        return verdict is TriageVerdict.Blocked blocked
            && blocked.RejectionAgainst(run.SuppliedOpenIssueNumbers) is { } rejection
                ? new WorkerOutcome.Failure(rejection, containerOutput, summary)
                : new WorkerOutcome.TriageCompleted(verdict, containerOutput, summary);
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

        if (Succeeded(status, summary))
        {
            return hasCommits
                ? await RepollForMrAsync(run, containerOutput, summary, askedQuestions, cancellationToken)
                : ResolveSucceededNoCommits(run, containerOutput, summary, askedQuestions);
        }

        FailureReason failureReason = ClassifyFailure(status, containerOutput, summary, run.Role);

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
        FailureReason? classified = TryClassifyParseResult(parseResult, run.Role);

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

    /// <summary>
    /// Whether the run succeeded: its container exited 0 and its result line does not report <c>is_error</c>.
    /// The CLI can exit 0 while reporting <c>is_error</c> (e.g. an expired OAuth session); such a run did no work.
    /// </summary>
    private static bool Succeeded(WorkerStatus? status, RunResultSummary? summary)
        => status?.ExitCode == 0 && summary is not { IsError: true };

    /// <summary>
    /// Classifies a run that did not succeed. A specific category from the output wins, so a usage, credit or auth
    /// failure is never reported as a generic error.
    /// </summary>
    private FailureReason ClassifyFailure(
        WorkerStatus? status,
        string? containerOutput,
        RunResultSummary? summary,
        WorkerRole role)
    {
        ContainerOutputParseResult parseResult = containerOutputParser.Parse(containerOutput);

        if (TryClassifyParseResult(parseResult, role) is { } classified)
        {
            return classified;
        }

        string? errorText = summary is { IsError: true } ? RedactedResultText(summary) : null;

        if (status?.ExitCode == 0)
        {
            return new FailureReason.ContainerError(errorText is null ? ErrorResultMessage : $"{ErrorResultMessage}: {errorText}");
        }

        return parseResult switch
        {
            ContainerOutputParseResult.NoResultLine => ClassifyNoResultLine(status, agentStarted: false),
            ContainerOutputParseResult.AgentStartedNoResultLine => ClassifyNoResultLine(status, agentStarted: true),
            _ => new FailureReason.NonZeroExit(status?.ExitCode ?? -1, errorText),
        };
    }

    /// <summary>
    /// The result line's text made safe to persist as a failure reason: secrets redacted, one line, and bounded
    /// by <see cref="MaxErrorResultTextLength"/>. <see langword="null"/> when the result line has no text.
    /// </summary>
    private static string? RedactedResultText(RunResultSummary summary)
    {
        string? resultText = summary.ResultText?.Trim();

        if (string.IsNullOrEmpty(resultText))
        {
            return null;
        }

        string redacted = SecretRedactor.Redact(resultText).ReplaceLineEndings(" ");

        return redacted.Length > MaxErrorResultTextLength ? redacted[..MaxErrorResultTextLength] : redacted;
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
    /// Foundry are container errors. Otherwise a run whose agent started is a plain non-zero exit,
    /// and one whose agent never started is a bootstrap failure.
    /// </summary>
    private static FailureReason ClassifyNoResultLine(WorkerStatus? status, bool agentStarted)
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

        return agentStarted
            ? new FailureReason.NonZeroExit(status.ExitCode ?? -1)
            : new FailureReason.WorkerBootstrapFailed(HeuristicBootstrapDetail);
    }

    /// <summary>A run resolved through its branch: the implement paths, which look up its merge request and commits.</summary>
    private readonly record struct BranchedRun(IResolvableRun Run, BranchName BranchName)
    {
        public MonitoredRepositoryId MonitoredRepositoryId => Run.MonitoredRepositoryId;

        public WorkerRole Role => Run.Role;
    }
}
