using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.HostPauses;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using WorkerRunFailedEvent = Foundry.Modules.Workers.Contracts.WorkerRunFailed;

namespace Foundry.Modules.Workers.Features.Publishing;

/// <summary>
/// Publishes a run whose container ended (ADR 0100): the Publisher pushes its Run bundle, code opens the pull request
/// or comments on the open one, and the outcome is resolved and applied. Each step is recorded on the run, so a retry
/// after a failure resumes where it stopped and never pushes, opens or comments twice.
/// </summary>
internal sealed class WorkerRunPublisher(
    DbContext dbContext,
    IPublisher publisher,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IRunPublishingCommands runPublishingCommands,
    IPostExitProviderQueries postExitProviderQueries,
    IIssueQueries issueQueries,
    IGlobalSettingsQueries settingsQueries,
    IContainerOutputParser containerOutputParser,
    WorkerOutcomeResolver resolver,
    WorkerOutcomeApplier outcomeApplier,
    IWorkerOrchestrator orchestrator,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IDomainEventDispatcher domainEventDispatcher,
    ISystemNotificationBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<WorkerRunPublisher> logger)
{
    internal const string MissingTokenReason = "No account token is configured for the repository.";

    /// <summary>Runs every publish step still to do for <paramref name="run"/>.</summary>
    public async Task PublishAsync(PublishingRun run, CancellationToken cancellationToken)
    {
        if (!run.HasPublished && !await PushAsync(run, cancellationToken))
        {
            return;
        }

        if (run.PullRequestSettledAt is null && !await SettlePullRequestAsync(run, cancellationToken))
        {
            return;
        }

        await ResolveAsync(run, cancellationToken);
    }

    /// <summary>Starts the scheduled retry of a publish that failed transiently.</summary>
    public async Task<PublishingRun> RetryAsync(PublishFailedRun failed, CancellationToken cancellationToken)
    {
        PublishingRun publishing = failed.RetryPublish(timeProvider.GetUtcNow());
        await integrationEventDispatcher.DispatchAsync(
            [new WorkerRunPublishPhaseChanged(failed.Id, failed.IssueId.Value, WorkerRunPublishPhase.Publishing)],
            cancellationToken);
        await dbContext.TransitionAsync(failed, publishing, domainEventDispatcher, cancellationToken);
        return publishing;
    }

    /// <summary>
    /// The Hand-off volume of an unpublished run reached its time to live: it is deleted and the run discarded, and its
    /// issue resumes from whatever the branch holds.
    /// </summary>
    public async Task DiscardAsync<TRun>(TRun run, CancellationToken cancellationToken)
        where TRun : UnpublishedRun, IDiscardableRun
    {
        await RunDiscarder.DiscardAsync(dbContext, orchestrator, domainEventDispatcher, run, run.Discard(), cancellationToken);

        logger.LogWarning(
            "Worker run {WorkerRunId} was discarded: its commits were not published within {TimeToLive}.",
            run.Id,
            PublishRetryPolicy.HandOffTimeToLive);
    }

    private async Task<bool> PushAsync(PublishingRun run, CancellationToken cancellationToken)
    {
        // A run dispatched before runs were prepared has no base to check a Run bundle against; it pushed its own work.
        if (run.Base is not { } runBase)
        {
            run.RecordPublished(new PublishResult.NothingToPublish(), timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Resolved at publish time rather than carried on any event: outbox payloads are plaintext (#800).
        RepositoryDispatchInfo? dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(
            run.MonitoredRepositoryId,
            cancellationToken);

        if (dispatchInfo?.AccountToken is not { Length: > 0 } gitPat)
        {
            await FailPublishAsync(run, MissingTokenReason, transient: false, cancellationToken);
            return false;
        }

        Result<CommitIdentity> identityResult = await runPublishingCommands.GetCommitIdentityAsync(
            run.MonitoredRepositoryId,
            cancellationToken);

        if (identityResult is not Result<CommitIdentity>.Success { Value: { } identity })
        {
            Error error = ((Result<CommitIdentity>.Failure)identityResult).Error;
            await FailPublishAsync(
                run,
                $"Reading the account identity failed: {error.Message}",
                RunPublishingErrors.IsTransient(error),
                cancellationToken);
            return false;
        }

        WorkerResourceLimitsSnapshot limits = await settingsQueries.GetWorkerResourceLimitsAsync(cancellationToken);

        PublishResult result = await publisher.PublishAsync(
            new PublishRequest(run.Id, dispatchInfo.CloneUrl, gitPat, run.BranchName, runBase, identity, limits),
            cancellationToken);

        if (result is PublishResult.Failed { Unreachable: true } unreachable)
        {
            await FailUnreachablePublishAsync(run, unreachable.Reason, dispatchInfo.Host, cancellationToken);
            return false;
        }

        if (result is PublishResult.Failed failed)
        {
            await FailPublishAsync(run, failed.Reason, failed.Transient, cancellationToken);
            return false;
        }

        if (result is PublishResult.SecretDetected secretDetected)
        {
            await BlockPublishAsync(run, secretDetected.Findings, cancellationToken);
            return false;
        }

        run.RecordPublished(result, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Worker run {WorkerRunId} published: {Result}.", run.Id, result.GetType().Name);
        return true;
    }

    /// <summary>
    /// Opens the pull request for a run that finished its turn, or posts the run's summary on the open one. A run that
    /// failed, was stopped, asked a question or diverged publishes to its branch only.
    /// </summary>
    private async Task<bool> SettlePullRequestAsync(PublishingRun run, CancellationToken cancellationToken)
    {
        if (!run.Exit.FinishedCleanly
            || run.Verdict is { Status: RunVerdictStatus.NeedsInput }
            || run.DivergedSideBranch is not null)
        {
            return await RecordPullRequestSettledAsync(run, cancellationToken);
        }

        Result<MergeRequestByBranch> mergeRequest = await postExitProviderQueries.GetMergeRequestByBranchAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        if (mergeRequest is Result<MergeRequestByBranch>.Failure lookupFailure)
        {
            await FailPublishAsync(run, $"Looking up the pull request failed: {lookupFailure.Error.Message}", transient: true, cancellationToken);
            return false;
        }

        MergeRequestByBranch existing = ((Result<MergeRequestByBranch>.Success)mergeRequest).Value;

        Result step = existing.Presence switch
        {
            MergeRequestPresence.None => await OpenPullRequestAsync(run, cancellationToken),
            MergeRequestPresence.Open when run.PublishedHead is not null && existing.WebUrl is { } url =>
                await PostSummaryAsync(run, url, cancellationToken),
            _ => Result.Ok(),
        };

        if (step is Result.Failure stepFailure)
        {
            await FailPublishAsync(
                run,
                stepFailure.Error.Message,
                RunPublishingErrors.IsTransient(stepFailure.Error),
                cancellationToken);
            return false;
        }

        return await RecordPullRequestSettledAsync(run, cancellationToken);
    }

    private async Task<Result> OpenPullRequestAsync(PublishingRun run, CancellationToken cancellationToken)
    {
        int branchCommits = run.PublishedBranchCommitCount ?? await CountBranchCommitsAsync(run, cancellationToken);

        // A branch with nothing beyond the default branch has nothing to propose.
        if (branchCommits == 0)
        {
            return Result.Ok();
        }

        Result<IssueDetail> issueResult = await issueQueries.GetIssueDetailAsync(run.IssueId, cancellationToken);
        if (issueResult is not Result<IssueDetail>.Success { Value: { } issue })
        {
            return Result.Fail(RunPublishingErrors.Transient("Reading the issue for the pull request failed."));
        }

        PullRequestText text = PullRequestText.Build(
            KindOf(issue.Kind.Kind),
            PullRequestSummary.TryCreate(run.PullRequestSummary),
            PullRequestDescription.TryCreate(run.PullRequestBody),
            issue.Title,
            issue.IssueNumber,
            run.PublishedCommitSubjects,
            run.RevertedFiles);

        Result<string> opened = await runPublishingCommands.OpenPullRequestAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            text.Title,
            text.Body,
            cancellationToken);

        if (opened is Result<string>.Failure openFailure)
        {
            return Result.Fail(new Error(openFailure.Error.Code, $"Opening the pull request failed: {openFailure.Error.Message}"));
        }

        logger.LogInformation(
            "Worker run {WorkerRunId} opened pull request {PullRequestUrl}.",
            run.Id,
            ((Result<string>.Success)opened).Value);
        return Result.Ok();
    }

    // A later run on a branch with an open pull request never overwrites the body a reviewer may have edited.
    private async Task<Result> PostSummaryAsync(PublishingRun run, string pullRequestUrl, CancellationToken cancellationToken)
    {
        string body = PullRequestText.SummaryComment(
            PullRequestDescription.TryCreate(run.PullRequestBody),
            run.PublishedCommitSubjects,
            run.RevertedFiles);

        Result posted = await runPublishingCommands.PostRunSummaryAsync(
            run.MonitoredRepositoryId,
            pullRequestUrl,
            body,
            cancellationToken);

        return posted is Result.Failure postFailure
            ? Result.Fail(new Error(postFailure.Error.Code, $"Posting the run summary failed: {postFailure.Error.Message}"))
            : posted;
    }

    private async Task<int> CountBranchCommitsAsync(PublishingRun run, CancellationToken cancellationToken)
    {
        Result<BranchCommitSummary> summary = await postExitProviderQueries.GetBranchCommitSummaryAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        return summary is Result<BranchCommitSummary>.Success { Value: { } commits } ? commits.CommitCount : 0;
    }

    private async Task<bool> RecordPullRequestSettledAsync(PublishingRun run, CancellationToken cancellationToken)
    {
        run.RecordPullRequestSettled(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ResolveAsync(PublishingRun run, CancellationToken cancellationToken)
    {
        WorkerOutcome outcome = await ResolveOutcomeAsync(run, cancellationToken);

        if (outcome is WorkerOutcome.Indeterminate indeterminate)
        {
            logger.LogWarning(
                "Worker run {WorkerRunId} is published but its outcome is indeterminate: {Error}. Leaving for next tick.",
                run.Id,
                indeterminate.Error);
            return;
        }

        await outcomeApplier.ApplyAsync(outcome, run, cancellationToken);
        await orchestrator.DeleteHandOffVolumeAsync(run.Id, cancellationToken);
    }

    private async Task<WorkerOutcome> ResolveOutcomeAsync(PublishingRun run, CancellationToken cancellationToken)
    {
        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(run.ContainerOutput);

        // The run's work is safe on a side branch; its issue waits for a person to merge it (or retry).
        if (run.DivergedSideBranch is not null)
        {
            return new WorkerOutcome.Unchanged(run.BranchName, run.ContainerOutput, summary);
        }

        switch (run.StopReason)
        {
            case RunStopReason.TimedOut:
                return await StoppedRunOutcomeBuilder.BuildAsync(
                    run,
                    new FailureReason.TimedOut(),
                    run.ContainerOutput,
                    summary,
                    postExitProviderQueries,
                    cancellationToken);

            case RunStopReason.Cancelled:
                return await resolver.TryResolveExistingPullRequestAsync(run, run.ContainerOutput, summary, cancellationToken)
                    ?? await StoppedRunOutcomeBuilder.BuildAsync(
                        run,
                        new FailureReason.Cancelled(),
                        run.ContainerOutput,
                        summary,
                        postExitProviderQueries,
                        cancellationToken);

            default:
                WorkerStatus? status = run.ExitCode is null
                    ? null
                    : new WorkerStatus(IsRunning: false, run.ExitCode, run.ExitedAt, run.OomKilled);
                return await resolver.ResolveAsync(run, status, run.ContainerOutput, run.Verdict, cancellationToken);
        }
    }

    // The findings are stored on the run; the event and the log carry only their number, never what they matched.
    private async Task BlockPublishAsync(
        PublishingRun run,
        IReadOnlyList<SecretFinding> findings,
        CancellationToken cancellationToken)
    {
        PublishBlockedRun blocked = run.BlockPublish(findings, timeProvider.GetUtcNow());

        await integrationEventDispatcher.DispatchAsync(
            [
                new WorkerRunPublishPhaseChanged(run.Id, run.IssueId.Value, Phase: null),
                new WorkerRunPublishBlocked(run.Id, run.IssueId.Value, run.BranchName.Value, findings.Count),
            ],
            cancellationToken);
        await dbContext.TransitionAsync(run, blocked, domainEventDispatcher, cancellationToken);

        logger.LogWarning(
            "Publishing worker run {WorkerRunId} was blocked: the diff gate found {FindingCount} possible secret(s), so nothing was pushed.",
            run.Id,
            findings.Count);
    }

    /// <summary>
    /// The Publisher could not reach the provider: the attempt fails transiently, and the run's host is paused in the
    /// same save, as a Prepare <c>provider_unreachable</c> pauses it. Until a probe clears the pause, the publishing loop
    /// holds this run's retry and every other publish to the host, so an outage spends no attempts (#885).
    /// </summary>
    private async Task FailUnreachablePublishAsync(
        PublishingRun run,
        string reason,
        string host,
        CancellationToken cancellationToken)
    {
        bool paused = await HostPauseStaging.StageAsync(dbContext, host, timeProvider.GetUtcNow(), logger, cancellationToken);

        await FailPublishAsync(run, reason, transient: true, cancellationToken);

        if (paused)
        {
            logger.LogWarning(
                "Dispatch and publishing to {Host} are paused: its provider is unreachable. A probe re-checks it and resumes them.",
                host);
            await HostPauseBroadcast.SendAsync(broadcaster, hostPaused: true, logger, cancellationToken);
        }
    }

    private async Task FailPublishAsync(PublishingRun run, string reason, bool transient, CancellationToken cancellationToken)
    {
        string redacted = SecretRedactor.Redact(reason);
        PublishFailedRun failed = run.FailPublish(redacted, transient, timeProvider.GetUtcNow());

        if (failed.IsExhausted)
        {
            FailureReason failure = new FailureReason.PublishFailed(redacted);
            await integrationEventDispatcher.DispatchAsync(
                [
                    new WorkerRunPublishPhaseChanged(run.Id, run.IssueId.Value, Phase: null),
                    new WorkerRunFailedEvent(run.Id, run.IssueId.Value, failure.Summary, failure.CategoryToken, run.BranchName.Value),
                ],
                cancellationToken);
        }
        else
        {
            await integrationEventDispatcher.DispatchAsync(
                [new WorkerRunPublishPhaseChanged(run.Id, run.IssueId.Value, WorkerRunPublishPhase.Retrying)],
                cancellationToken);
        }

        await dbContext.TransitionAsync(run, failed, domainEventDispatcher, cancellationToken);

        logger.LogWarning(
            "Publishing worker run {WorkerRunId} failed (attempt {Attempt} of {MaxAttempts}, {Kind}): {Reason}",
            run.Id,
            failed.FailedPublishAttempts,
            PublishRetryPolicy.MaxAttempts,
            transient ? "transient" : "permanent",
            redacted);
    }

    private static PullRequestKind KindOf(string? kind) => kind switch
    {
        "feature" => PullRequestKind.Feature,
        "bug" => PullRequestKind.Bug,
        "refactor" => PullRequestKind.Refactor,
        "documentation" => PullRequestKind.Documentation,
        _ => PullRequestKind.Unclassified,
    };
}
